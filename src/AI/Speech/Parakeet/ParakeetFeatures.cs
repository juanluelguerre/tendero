namespace ElGuerre.Tendero.AI.Speech.Parakeet;

/// <summary>
/// Parakeet's input, as transformers 5.9.0 computes it (<c>ParakeetFeatureExtractor</c>):
/// pre-emphasis 0.97; a centred STFT of 512 points every 160 samples through a
/// symmetric Hann window of 400; the power spectrum through librosa's Slaney mel
/// bank of 128; the log with a guard of 2⁻²⁴; and each mel band normalised over the
/// frames that hold audio. Written here, not exported, and held to the maker's own
/// numbers (<c>parakeet-fixture.json</c>).
/// </summary>
public static class ParakeetFeatures
{
    public const int Mels = 128;
    public const int SampleRate = 16_000;
    private const int FftSize = 512;
    private const int Hop = 160;
    private const int WindowLength = 400;
    private const int Bins = FftSize / 2 + 1;
    private const double Preemphasis = 0.97;
    private const double LogGuard = 1.0 / (1 << 24);
    private const double Epsilon = 1e-5;

    // The Slaney scale: linear to 1 kHz, logarithmic above (librosa's hz_to_mel, htk=False).
    // Before the mel bank uses it: static fields start in the order written.
    private const double LinearStep = 200.0 / 3;
    private const double LogStart = 1000.0 / LinearStep;
    private static readonly double LogStep = Math.Log(6.4) / 27;


    /// <summary>The mel bank's sum, for the fixture's check that it is librosa's.</summary>
    public static double MelBankSum
    {
        get
        {
            var sum = 0.0;
            foreach (var weight in SlaneyMelBank())
                sum += weight;
            return sum;
        }
    }

    /// <summary>
    /// The features of 16 kHz mono samples in [-1, 1]: <c>Frames</c> rows of
    /// <see cref="Mels"/> values, row by row, of which the first <c>Valid</c> hold
    /// audio and the rest are zero — the attention mask's ones.
    /// </summary>
    public static (float[] Values, int Frames, int Valid) Of(ReadOnlySpan<float> samples)
    {
        var length = samples.Length;
        var emphasised = new double[length];
        for (var i = 0; i < length; i++)
            emphasised[i] = i == 0 ? samples[0] : samples[i] - Preemphasis * samples[i - 1];

        var frames = 1 + length / Hop;
        var valid = Math.Max(0, (length + FftSize / 2 * 2 - FftSize) / Hop);
        var values = new float[frames * Mels];
        var real = new double[FftSize];
        var imaginary = new double[FftSize];
        var power = new double[Bins];
        // Built for each call, not held in static fields: microseconds beside the encoder,
        // and a static initialiser runs once, out of a mutation tool's reach (phase 14's
        // close found 64 of this file's survivors there, every one held by the fixture).
        var window = HannSymmetric();
        var melBank = SlaneyMelBank();

        for (var frame = 0; frame < frames; frame++)
        {
            // Centred: the signal padded with FftSize/2 zeros on each side, and the
            // window of 400 in the middle of the 512 points.
            var start = frame * Hop - FftSize / 2;
            for (var n = 0; n < FftSize; n++)
            {
                var at = start + n;
                real[n] = at >= 0 && at < length ? emphasised[at] * window[n] : 0;
                imaginary[n] = 0;
            }

            Fft.Transform(real, imaginary);
            for (var k = 0; k < Bins; k++)
                power[k] = real[k] * real[k] + imaginary[k] * imaginary[k];

            for (var mel = 0; mel < Mels; mel++)
            {
                var energy = 0.0;
                for (var k = 0; k < Bins; k++)
                    energy += melBank[mel, k] * power[k];
                values[frame * Mels + mel] = (float)Math.Log(energy + LogGuard);
            }
        }

        Normalise(values, frames, valid);
        return (values, frames, valid);
    }

    /// <summary>Each mel band to zero mean and unit deviation over the valid frames; the rest to zero.</summary>
    private static void Normalise(float[] values, int frames, int valid)
    {
        for (var mel = 0; mel < Mels; mel++)
        {
            var mean = 0.0;
            for (var frame = 0; frame < valid; frame++)
                mean += values[frame * Mels + mel];
            mean /= Math.Max(valid, 1);

            var variance = 0.0;
            for (var frame = 0; frame < valid; frame++)
            {
                var difference = values[frame * Mels + mel] - mean;
                variance += difference * difference;
            }
            var deviation = Math.Sqrt(variance / Math.Max(valid - 1, 1));

            for (var frame = 0; frame < frames; frame++)
                values[frame * Mels + mel] = frame < valid ? (float)((values[frame * Mels + mel] - mean) / (deviation + Epsilon)) : 0f;
        }
    }

    /// <summary><c>torch.hann_window(400, periodic=False)</c>, centred in the 512 points as <c>torch.stft</c> pads it.</summary>
    private static double[] HannSymmetric()
    {
        var window = new double[FftSize];
        var offset = (FftSize - WindowLength) / 2;
        for (var n = 0; n < WindowLength; n++)
            window[offset + n] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * n / (WindowLength - 1));
        return window;
    }

    /// <summary><c>librosa.filters.mel(sr=16000, n_fft=512, n_mels=128, fmin=0, fmax=8000, norm="slaney")</c>.</summary>
    private static float[,] SlaneyMelBank()
    {
        var melPoints = new double[Mels + 2];
        var top = HzToMel(SampleRate / 2.0);
        for (var i = 0; i < melPoints.Length; i++)
            melPoints[i] = MelToHz(top * i / (Mels + 1));

        var bank = new float[Mels, Bins];
        for (var mel = 0; mel < Mels; mel++)
        {
            var lowerWidth = melPoints[mel + 1] - melPoints[mel];
            var upperWidth = melPoints[mel + 2] - melPoints[mel + 1];
            var norm = 2.0 / (melPoints[mel + 2] - melPoints[mel]);
            for (var k = 0; k < Bins; k++)
            {
                var frequency = k * (SampleRate / 2.0) / (Bins - 1);
                var lower = (frequency - melPoints[mel]) / lowerWidth;
                var upper = (melPoints[mel + 2] - frequency) / upperWidth;
                bank[mel, k] = (float)(Math.Max(0, Math.Min(lower, upper)) * norm);
            }
        }

        return bank;
    }

    private static double HzToMel(double hz) =>
        hz < 1000 ? hz / LinearStep : LogStart + Math.Log(hz / 1000) / LogStep;

    private static double MelToHz(double mel) =>
        mel < LogStart ? mel * LinearStep : 1000 * Math.Exp(LogStep * (mel - LogStart));
}

/// <summary>An in-place radix-2 FFT, for the 512 points the features need.</summary>
internal static class Fft
{
    public static void Transform(double[] real, double[] imaginary)
    {
        var n = real.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
            }
        }

        for (var size = 2; size <= n; size <<= 1)
        {
            var angle = -2 * Math.PI / size;
            for (var start = 0; start < n; start += size)
            {
                for (var k = 0; k < size / 2; k++)
                {
                    var (cos, sin) = (Math.Cos(angle * k), Math.Sin(angle * k));
                    var even = start + k;
                    var odd = even + size / 2;
                    var oddReal = real[odd] * cos - imaginary[odd] * sin;
                    var oddImaginary = real[odd] * sin + imaginary[odd] * cos;
                    real[odd] = real[even] - oddReal;
                    imaginary[odd] = imaginary[even] - oddImaginary;
                    real[even] += oddReal;
                    imaginary[even] += oddImaginary;
                }
            }
        }
    }
}
