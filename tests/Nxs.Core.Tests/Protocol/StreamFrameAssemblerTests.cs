using Nxs.Core.Protocol;
using Nxs.TestKit;

namespace Nxs.Core.Tests.Protocol;

/// <summary>
/// 부분 수신 불변 (CLAUDE.md §4.2 — 프레임 파서는 1바이트 주입 테스트 필수).
/// 프레이밍 규칙은 주입되므로 이 테스트는 XGT 프레임 세부에 의존하지 않는다.
/// </summary>
public class StreamFrameAssemblerTests
{
    private static StreamFrameAssembler NewAssembler(int maxFrameLength = 4096)
        => new(new TestOnlyLengthPrefixFraming(), maxFrameLength);

    [Fact]
    public void SingleCompleteFrameIsEmitted()
    {
        var frame = TestOnlyLengthPrefixFraming.BuildFrame(8, seed: 0x10);
        var asm = NewAssembler();

        var frames = asm.Push(frame);

        Assert.Equal(frame, Assert.Single(frames));
        Assert.Equal(0, asm.BufferedByteCount);
    }

    [Fact]
    public void ByteByByteInjectionYieldsIdenticalFrameAtFinalByte()
    {
        var frame = TestOnlyLengthPrefixFraming.BuildFrame(12, seed: 0x40);
        var asm = NewAssembler();

        // 마지막 바이트 이전에는 어떤 프레임도 나오지 않아야 한다.
        for (var i = 0; i < frame.Length - 1; i++)
        {
            Assert.Empty(asm.Push(frame.AsSpan(i, 1)));
        }

        var emitted = asm.Push(frame.AsSpan(frame.Length - 1, 1));

        Assert.Equal(frame, Assert.Single(emitted));
        Assert.Equal(0, asm.BufferedByteCount);
    }

    [Fact]
    public void EverySplitPointProducesTheSameFrame()
    {
        var frame = TestOnlyLengthPrefixFraming.BuildFrame(20, seed: 0x77);

        for (var split = 0; split <= frame.Length; split++)
        {
            var asm = NewAssembler();
            var collected = new List<byte[]>();
            collected.AddRange(asm.Push(frame.AsSpan(0, split)));
            collected.AddRange(asm.Push(frame.AsSpan(split)));

            Assert.Equal(frame, Assert.Single(collected));
        }
    }

    [Fact]
    public void TwoFramesInOneChunkAreBothEmittedInOrder()
    {
        var a = TestOnlyLengthPrefixFraming.BuildFrame(4, seed: 0x01);
        var b = TestOnlyLengthPrefixFraming.BuildFrame(6, seed: 0x81);
        var chunk = a.Concat(b).ToArray();
        var asm = NewAssembler();

        var frames = asm.Push(chunk).ToList();

        Assert.Equal(2, frames.Count);
        Assert.Equal(a, frames[0]);
        Assert.Equal(b, frames[1]);
    }

    [Fact]
    public void SecondFrameSplitAcrossChunkBoundaryIsReassembled()
    {
        var a = TestOnlyLengthPrefixFraming.BuildFrame(4, seed: 0x01);
        var b = TestOnlyLengthPrefixFraming.BuildFrame(6, seed: 0x81);
        var stream = a.Concat(b).ToArray();
        var asm = NewAssembler();

        // a 전체 + b 의 앞 3바이트
        var first = asm.Push(stream.AsSpan(0, a.Length + 3)).ToList();
        var second = asm.Push(stream.AsSpan(a.Length + 3)).ToList();

        Assert.Equal(a, Assert.Single(first));
        Assert.Equal(b, Assert.Single(second));
    }

    [Fact]
    public void ZeroLengthPayloadFrameIsEmitted()
    {
        var frame = TestOnlyLengthPrefixFraming.BuildFrame(0, seed: 0);
        var asm = NewAssembler();

        Assert.Equal(frame, Assert.Single(asm.Push(frame)));
    }

    [Fact]
    public void EmptyPushEmitsNothingAndKeepsBuffer()
    {
        var asm = NewAssembler();
        asm.Push(new byte[] { TestOnlyLengthPrefixFraming.Magic0 });

        Assert.Empty(asm.Push(ReadOnlySpan<byte>.Empty));
        Assert.Equal(1, asm.BufferedByteCount);
    }

    [Fact]
    public void UndecodableHeaderThrowsFramingException()
    {
        var asm = NewAssembler();

        Assert.Throws<FramingException>(() => asm.Push(new byte[] { 0x00, 0x00, 0x00, 0x00 }).ToList());
    }

    [Fact]
    public void DeclaredLengthOverMaxThrowsFramingException()
    {
        var asm = NewAssembler(maxFrameLength: 16);
        var frame = TestOnlyLengthPrefixFraming.BuildFrame(64, seed: 0x22);

        Assert.Throws<FramingException>(() => asm.Push(frame).ToList());
    }

    [Fact]
    public void ResetDiscardsBufferedPartialFrame()
    {
        var frame = TestOnlyLengthPrefixFraming.BuildFrame(8, seed: 0x33);
        var asm = NewAssembler();
        asm.Push(frame.AsSpan(0, 5));
        Assert.Equal(5, asm.BufferedByteCount);

        asm.Reset();

        Assert.Equal(0, asm.BufferedByteCount);
        Assert.Equal(frame, Assert.Single(asm.Push(frame)));
    }

    [Fact]
    public void ManyFramesStreamedInRandomChunkSizesAllArriveIntact()
    {
        var expected = new List<byte[]>();
        var stream = new List<byte>();
        for (var i = 0; i < 25; i++)
        {
            var f = TestOnlyLengthPrefixFraming.BuildFrame(payloadLength: 1 + i * 3, seed: (byte)(i * 7));
            expected.Add(f);
            stream.AddRange(f);
        }

        var asm = NewAssembler();
        var actual = new List<byte[]>();
        // 결정적 청크 크기 시퀀스 (재현 가능 — 난수 미사용)
        var offset = 0;
        var sizes = new[] { 1, 7, 3, 64, 2, 13, 1, 128, 5 };
        var s = 0;
        while (offset < stream.Count)
        {
            var take = Math.Min(sizes[s++ % sizes.Length], stream.Count - offset);
            actual.AddRange(asm.Push(CollectionsMarshalSpan(stream, offset, take)));
            offset += take;
        }

        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i], actual[i]);
        }
    }

    private static ReadOnlySpan<byte> CollectionsMarshalSpan(List<byte> list, int start, int length)
        => list.GetRange(start, length).ToArray();
}
