using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Nxs.Core.Protocol.Xgt;

/// <summary>프레임 한 조각이 무엇인지.</summary>
public enum FrameFieldKind
{
    /// <summary>애플리케이션 헤더(앞 20바이트)의 필드.</summary>
    Header,

    /// <summary>데이터부의 고정 필드(명령·타입·블록 수 등).</summary>
    Command,

    /// <summary>변수명 구간.</summary>
    Name,

    /// <summary>값·데이터 구간.</summary>
    Value,

    /// <summary>해석하지 못한 잔여 바이트.</summary>
    Unknown,
}

/// <summary>프레임 안의 이름 붙은 한 구간.</summary>
/// <param name="Offset">프레임 시작부터의 바이트 위치.</param>
/// <param name="Length">바이트 수.</param>
/// <param name="Kind">구간 종류.</param>
/// <param name="Name">사람이 읽는 이름.</param>
/// <param name="Value">해석한 값(없으면 빈 문자열).</param>
/// <param name="Address">이 구간이 가리키는 주소 표기(해당 없으면 null).</param>
public sealed record FrameField(
    int Offset,
    int Length,
    FrameFieldKind Kind,
    string Name,
    string Value = "",
    string? Address = null)
{
    /// <summary>구간의 끝(배타적).</summary>
    public int End => Offset + Length;

    /// <summary>이 구간이 해당 바이트 위치를 포함하는지.</summary>
    public bool Contains(int offset) => offset >= Offset && offset < End;

    /// <summary>위치 표기 — <c>20 ~ 21 (2바이트)</c>.</summary>
    public string RangeText => Length == 1
        ? $"{Offset} (1바이트)"
        : $"{Offset} ~ {End - 1} ({Length}바이트)";
}

/// <summary>
/// XGT 프레임을 구간별로 쪼개 "어디부터 어디까지가 무엇인지" 알려 준다.
/// </summary>
/// <remarks>
/// <para>
/// 진단용 **읽기 전용 분석기**다. 코덱과 달리 메모리를 건드리지 않고, 무엇을 만나도 예외를 던지지 않는다 —
/// 깨진 프레임일수록 봐야 할 이유가 큰데 분석기가 죽으면 볼 수가 없다.
/// 해석하지 못한 바이트는 버리지 않고 <see cref="FrameFieldKind.Unknown"/> 구간으로 남긴다.
/// </para>
/// <para>
/// 레이아웃 지식은 <see cref="XgtFenetCodec"/> 와 같은 근거(spec/xgt-fenet-reference.md)를 쓴다.
/// 코덱이 실제로 어떻게 읽는지와 화면 설명이 어긋나면 진단이 오히려 방해가 되므로,
/// 두 쪽을 같은 프레임으로 함께 검증한다(XgtFrameAnatomyTests).
/// </para>
/// </remarks>
public static class XgtFrameAnatomy
{
    private const int HeaderLength = 20;

    /// <summary>프레임을 구간 목록으로 쪼갠다. 어떤 입력에도 예외를 던지지 않는다.</summary>
    public static IReadOnlyList<FrameField> Describe(ReadOnlySpan<byte> frame)
    {
        var fields = new List<FrameField>(16);

        if (frame.IsEmpty)
        {
            return fields;
        }

        if (frame.Length < HeaderLength)
        {
            fields.Add(new FrameField(0, frame.Length, FrameFieldKind.Unknown,
                "잘린 헤더", $"헤더는 {HeaderLength}바이트인데 {frame.Length}바이트뿐입니다"));
            return fields;
        }

        AddHeader(fields, frame);

        var data = frame[HeaderLength..];
        if (data.Length < 8)
        {
            AddRemainder(fields, frame, HeaderLength, "데이터부 (너무 짧아 해석 불가)");
            return fields;
        }

        var command = BinaryPrimitives.ReadUInt16LittleEndian(data);
        var dataType = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);

        fields.Add(new FrameField(HeaderLength, 2, FrameFieldKind.Command,
            "명령", $"0x{command:X4} — {CommandName(command)}"));
        fields.Add(new FrameField(HeaderLength + 2, 2, FrameFieldKind.Command,
            "데이터 타입", $"0x{dataType:X4} — {DataTypeName(dataType)}"));
        fields.Add(new FrameField(HeaderLength + 4, 2, FrameFieldKind.Command, "예약", "0x0000"));

        var cursor = 6;
        var isResponse = command is 0x0055 or 0x0059;

        if (isResponse)
        {
            var status = BinaryPrimitives.ReadUInt16LittleEndian(data[cursor..]);
            fields.Add(new FrameField(HeaderLength + cursor, 2, FrameFieldKind.Command,
                "에러 상태", status == 0 ? "0x0000 — 정상" : $"0x{status:X4} — 거절"));
            cursor += 2;

            if (data.Length - cursor < 2)
            {
                AddRemainder(fields, frame, HeaderLength + cursor, "잘린 블록 수");
                return fields;
            }
        }

        var blockCount = BinaryPrimitives.ReadUInt16LittleEndian(data[cursor..]);
        fields.Add(new FrameField(HeaderLength + cursor, 2, FrameFieldKind.Command,
            "블록 수", blockCount.ToString(CultureInfo.InvariantCulture)));
        cursor += 2;

        if (isResponse)
        {
            ReadResponseBlocks(fields, frame, data, ref cursor, blockCount);
        }
        else
        {
            ReadRequestBlocks(fields, frame, data, ref cursor, command, dataType, blockCount);
        }

        // 앞의 분석이 어디서 멈췄든 남은 바이트는 반드시 한 구간으로 덮는다 —
        // cursor 가 아니라 **실제로 붙인 마지막 구간의 끝**을 기준으로 삼아야
        // 중간에 잘린 블록을 이미 기록한 경우에도 겹치지 않는다.
        AddRemainder(fields, frame, fields.Count > 0 ? fields[^1].End : 0, "남은 바이트");
        return fields;
    }

    /// <summary>구간 목록에서 특정 주소를 가리키는 구간들을 고른다.</summary>
    public static IReadOnlyList<FrameField> FieldsFor(IEnumerable<FrameField> fields, string address)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return fields
            .Where(f => f.Address is not null
                && string.Equals(f.Address, address, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static void AddHeader(List<FrameField> fields, ReadOnlySpan<byte> frame)
    {
        var company = Encoding.ASCII.GetString(frame[..8]).TrimEnd('\0');
        var direction = frame[13];

        fields.Add(new FrameField(0, 10, FrameFieldKind.Header, "회사 ID", company));
        fields.Add(new FrameField(10, 2, FrameFieldKind.Header, "PLC 정보",
            $"0x{BinaryPrimitives.ReadUInt16LittleEndian(frame[10..]):X4}"));
        fields.Add(new FrameField(12, 1, FrameFieldKind.Header, "CPU 정보", $"0x{frame[12]:X2}"));
        fields.Add(new FrameField(13, 1, FrameFieldKind.Header, "방향", direction switch
        {
            0x33 => "0x33 — 요청 (마스터 → PLC)",
            0x11 => "0x11 — 응답 (PLC → 마스터)",
            _ => $"0x{direction:X2} — 알 수 없음",
        }));
        fields.Add(new FrameField(14, 2, FrameFieldKind.Header, "Invoke ID",
            BinaryPrimitives.ReadUInt16LittleEndian(frame[14..]).ToString(CultureInfo.InvariantCulture)));

        var declared = BinaryPrimitives.ReadUInt16LittleEndian(frame[16..]);
        var actual = frame.Length - HeaderLength;
        fields.Add(new FrameField(16, 2, FrameFieldKind.Header, "길이",
            declared == actual
                ? $"{declared} — 데이터부 실제 길이와 일치"
                : $"{declared} — 실제 데이터부는 {actual}바이트 (불일치)"));

        fields.Add(new FrameField(18, 1, FrameFieldKind.Header, "모듈 위치", $"0x{frame[18]:X2}"));

        byte sum = 0;
        for (var i = 0; i < 19; i++)
        {
            sum += frame[i];
        }

        fields.Add(new FrameField(19, 1, FrameFieldKind.Header, "BCC",
            frame[19] == sum
                ? $"0x{frame[19]:X2} — 헤더 0~18 합과 일치"
                : $"0x{frame[19]:X2} — 계산값 0x{sum:X2} 와 다름 (수신 시 검사하지 않음)"));
    }

    private static void ReadRequestBlocks(
        List<FrameField> fields, ReadOnlySpan<byte> frame, ReadOnlySpan<byte> data,
        ref int cursor, ushort command, ushort dataType, ushort blockCount)
    {
        var names = new List<string>(blockCount);

        for (var i = 0; i < blockCount; i++)
        {
            if (!TryReadName(fields, frame, data, ref cursor, i, blockCount, out var name))
            {
                return;
            }

            names.Add(name);
        }

        if (dataType == 0x0014)
        {
            // 연속 요청 — 읽기는 바이트 수, 쓰기는 (있을 수도 있는) 개수 필드 + 데이터.
            var address = names.Count > 0 ? names[0] : null;
            if (command == 0x0054)
            {
                if (data.Length - cursor >= 2)
                {
                    var count = BinaryPrimitives.ReadUInt16LittleEndian(data[cursor..]);
                    fields.Add(new FrameField(HeaderLength + cursor, 2, FrameFieldKind.Value,
                        "읽을 바이트 수", count.ToString(CultureInfo.InvariantCulture), address));
                    cursor += 2;
                }

                return;
            }

            AddContinuousWritePayload(fields, data, ref cursor, address);
            return;
        }

        if (command != 0x0058)
        {
            return;
        }

        AddIndividualWriteValues(fields, data, ref cursor, dataType, names);
    }

    /// <summary>
    /// 연속 쓰기의 개수 필드 유무를 코덱과 **같은 방식**(남은 길이 대조)으로 판별해 표시한다.
    /// </summary>
    private static void AddContinuousWritePayload(
        List<FrameField> fields, ReadOnlySpan<byte> data, ref int cursor, string? address)
    {
        var remaining = data.Length - cursor;
        if (remaining <= 0)
        {
            return;
        }

        if (remaining >= 2
            && BinaryPrimitives.ReadUInt16LittleEndian(data[cursor..]) == remaining - 2)
        {
            var declared = BinaryPrimitives.ReadUInt16LittleEndian(data[cursor..]);
            fields.Add(new FrameField(HeaderLength + cursor, 2, FrameFieldKind.Command,
                "데이터 길이", declared.ToString(CultureInfo.InvariantCulture), address));
            cursor += 2;
            remaining -= 2;
        }

        fields.Add(new FrameField(HeaderLength + cursor, remaining, FrameFieldKind.Value,
            "데이터", Hex.Format(data.Slice(cursor, remaining)), address));
        cursor += remaining;
    }

    /// <summary>
    /// 개별 쓰기 값 구간. 크기 필드 유무를 코덱과 같은 산술로 판별한다 —
    /// 화면 설명과 코덱의 해석이 어긋나면 진단이 오히려 방해가 된다.
    /// </summary>
    private static void AddIndividualWriteValues(
        List<FrameField> fields, ReadOnlySpan<byte> data, ref int cursor,
        ushort dataType, List<string> names)
    {
        var elementSize = ElementSize(dataType);
        var remaining = data.Length - cursor;
        var withSize = names.Count * (2 + elementSize);
        var withoutSize = names.Count * elementSize;

        var hasSizeField = remaining == withSize
            || (remaining != withoutSize && remaining > withSize);

        for (var i = 0; i < names.Count; i++)
        {
            if (hasSizeField)
            {
                if (data.Length - cursor < 2)
                {
                    return;
                }

                var size = BinaryPrimitives.ReadUInt16LittleEndian(data[cursor..]);
                fields.Add(new FrameField(HeaderLength + cursor, 2, FrameFieldKind.Command,
                    $"블록 {i + 1} 값 크기", size.ToString(CultureInfo.InvariantCulture), names[i]));
                cursor += 2;

                if (data.Length - cursor < size)
                {
                    return;
                }

                fields.Add(new FrameField(HeaderLength + cursor, size, FrameFieldKind.Value,
                    $"블록 {i + 1} 값", Hex.Format(data.Slice(cursor, size)), names[i]));
                cursor += size;
            }
            else
            {
                if (elementSize <= 0 || data.Length - cursor < elementSize)
                {
                    return;
                }

                fields.Add(new FrameField(HeaderLength + cursor, elementSize, FrameFieldKind.Value,
                    $"블록 {i + 1} 값", Hex.Format(data.Slice(cursor, elementSize)), names[i]));
                cursor += elementSize;
            }
        }
    }

    private static void ReadResponseBlocks(
        List<FrameField> fields, ReadOnlySpan<byte> frame, ReadOnlySpan<byte> data,
        ref int cursor, ushort blockCount)
    {
        for (var i = 0; i < blockCount; i++)
        {
            if (data.Length - cursor < 2)
            {
                AddRemainder(fields, frame, HeaderLength + cursor, $"잘린 블록 {i + 1}");
                return;
            }

            var size = BinaryPrimitives.ReadUInt16LittleEndian(data[cursor..]);
            fields.Add(new FrameField(HeaderLength + cursor, 2, FrameFieldKind.Command,
                $"블록 {i + 1} 크기", size.ToString(CultureInfo.InvariantCulture)));
            cursor += 2;

            if (data.Length - cursor < size)
            {
                AddRemainder(fields, frame, HeaderLength + cursor, $"잘린 블록 {i + 1} 데이터");
                return;
            }

            fields.Add(new FrameField(HeaderLength + cursor, size, FrameFieldKind.Value,
                $"블록 {i + 1} 데이터", Hex.Format(data.Slice(cursor, size))));
            cursor += size;
        }
    }

    private static bool TryReadName(
        List<FrameField> fields, ReadOnlySpan<byte> frame, ReadOnlySpan<byte> data,
        ref int cursor, int index, int blockCount, out string name)
    {
        name = string.Empty;

        if (data.Length - cursor < 2)
        {
            AddRemainder(fields, frame, HeaderLength + cursor, $"잘린 블록 {index + 1} 이름 길이");
            return false;
        }

        var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(data[cursor..]);
        if (data.Length - cursor - 2 < nameLength)
        {
            AddRemainder(fields, frame, HeaderLength + cursor, $"잘린 블록 {index + 1} 이름");
            return false;
        }

        name = Encoding.ASCII.GetString(data.Slice(cursor + 2, nameLength));

        var label = blockCount == 1 ? "이름 길이" : $"블록 {index + 1} 이름 길이";
        fields.Add(new FrameField(HeaderLength + cursor, 2, FrameFieldKind.Command,
            label, nameLength.ToString(CultureInfo.InvariantCulture), name));
        cursor += 2;

        fields.Add(new FrameField(HeaderLength + cursor, nameLength, FrameFieldKind.Name,
            blockCount == 1 ? "변수명" : $"블록 {index + 1} 변수명", name, name));
        cursor += nameLength;
        return true;
    }

    private static void AddRemainder(
        List<FrameField> fields, ReadOnlySpan<byte> frame, int offset, string name)
    {
        if (offset >= frame.Length)
        {
            return;
        }

        fields.Add(new FrameField(offset, frame.Length - offset, FrameFieldKind.Unknown,
            name, Hex.Format(frame[offset..])));
    }

    private static int ElementSize(ushort dataType) => dataType switch
    {
        0x0000 or 0x0001 => 1,
        0x0002 => 2,
        0x0003 => 4,
        0x0004 => 8,
        _ => 0,
    };

    private static string CommandName(ushort command) => command switch
    {
        0x0054 => "읽기 요청",
        0x0055 => "읽기 응답",
        0x0058 => "쓰기 요청",
        0x0059 => "쓰기 응답",
        _ => "알 수 없는 명령",
    };

    private static string DataTypeName(ushort dataType) => dataType switch
    {
        0x0000 => "비트",
        0x0001 => "바이트",
        0x0002 => "워드",
        0x0003 => "더블워드",
        0x0004 => "롱워드",
        0x0014 => "연속(블록)",
        _ => "알 수 없는 타입",
    };
}
