namespace Tapedeck.Core.Audio;

/// <summary>
/// Magnitude spectra of fixed-size frames: an iterative radix-2 FFT with a Hann window. Small and self-made, so the
/// sound analysis needs no package. One instance per frame size and thread, the buffers are reused.
/// </summary>
internal sealed class Fft
{
    private readonly int size;
    private readonly double[] window;
    private readonly double[] cos;
    private readonly double[] sin;
    private readonly int[] reversed;
    private readonly double[] re;
    private readonly double[] im;

    public Fft(int size)
    {
        if (size < 2 || (size & (size - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(size), size, "The frame size must be a power of two.");
        this.size = size;
        this.window = new double[size];
        for (var i = 0; i < size; i++)
            this.window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / size);
        this.cos = new double[size / 2];
        this.sin = new double[size / 2];
        for (var i = 0; i < size / 2; i++)
        {
            this.cos[i] = Math.Cos(-2 * Math.PI * i / size);
            this.sin[i] = Math.Sin(-2 * Math.PI * i / size);
        }

        var bits = (int)Math.Log2(size);
        this.reversed = new int[size];
        for (var i = 0; i < size; i++)
        {
            var r = 0;
            for (var b = 0; b < bits; b++)
                r |= ((i >> b) & 1) << (bits - 1 - b);
            this.reversed[i] = r;
        }
        this.re = new double[size];
        this.im = new double[size];
    }

    public int Size => this.size;

    /// <summary>Number of bins <see cref="Magnitudes"/> fills: 0 (DC) up to and including Nyquist.</summary>
    public int Bins => this.size / 2 + 1;

    /// <summary>
    /// The windowed frame <c>samples[offset .. offset + Size]</c> transformed; <paramref name="magnitudes"/> gets
    /// <see cref="Bins"/> values. Samples past the end count as silence.
    /// </summary>
    public void Magnitudes(ReadOnlySpan<float> samples, int offset, Span<double> magnitudes)
    {
        for (var i = 0; i < this.size; i++)
        {
            var index = offset + i;
            var value = index < samples.Length ? samples[index] : 0f;
            var target = this.reversed[i];
            this.re[target] = value * this.window[i];
            this.im[target] = 0;
        }

        for (var length = 2; length <= this.size; length <<= 1)
        {
            var half = length / 2;
            var step = this.size / length;
            for (var start = 0; start < this.size; start += length)
            {
                for (var k = 0; k < half; k++)
                {
                    var wr = this.cos[k * step];
                    var wi = this.sin[k * step];
                    var a = start + k;
                    var b = a + half;
                    var tr = this.re[b] * wr - this.im[b] * wi;
                    var ti = this.re[b] * wi + this.im[b] * wr;
                    this.re[b] = this.re[a] - tr;
                    this.im[b] = this.im[a] - ti;
                    this.re[a] += tr;
                    this.im[a] += ti;
                }
            }
        }

        for (var i = 0; i < this.Bins; i++)
            magnitudes[i] = Math.Sqrt(this.re[i] * this.re[i] + this.im[i] * this.im[i]);
    }
}
