namespace VoiceInputBuddy.Core.Audio;

public static class ImaAdpcmTables
{
    public static readonly int[] StepTable =
    [
        7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31,
        34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118, 130,
        143, 157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449,
        494, 544, 598, 658, 724, 796, 876, 963, 1060, 1166, 1282, 1411,
        1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026,
        4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442,
        11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623,
        27086, 29794, 32767
    ];

    public static readonly int[] IndexTable = [-1, -1, -1, -1, 2, 4, 6, 8];
}

public sealed class ImaAdpcmDecoder
{
    private int _predictor;
    private int _stepIndex;

    public int Predictor => _predictor;
    public int StepIndex => _stepIndex;
    public bool LowNibbleFirst { get; set; } = false;

    public void Synchronize(short syncPredictor, byte syncStepIndex)
    {
        _predictor = syncPredictor;
        _stepIndex = Math.Clamp((int)syncStepIndex, 0, 88);
    }

    public void Reset(int predictor = 0, int stepIndex = 0)
    {
        _predictor = Math.Clamp(predictor, -32768, 32767);
        _stepIndex = Math.Clamp(stepIndex, 0, 88);
    }

    public int Decode(ReadOnlySpan<byte> input, Span<short> output)
    {
        int required = input.Length * 2;
        if (output.Length < required)
        {
            throw new ArgumentException($"Output buffer too small: required {required}, available {output.Length}");
        }

        int outIdx = 0;
        for (int i = 0; i < input.Length; i++)
        {
            byte b = input[i];
            byte nibble1 = (byte)(LowNibbleFirst ? (b & 0x0F) : ((b >> 4) & 0x0F));
            byte nibble2 = (byte)(LowNibbleFirst ? ((b >> 4) & 0x0F) : (b & 0x0F));

            output[outIdx++] = DecodeSample(nibble1);
            output[outIdx++] = DecodeSample(nibble2);
        }

        return outIdx;
    }

    private short DecodeSample(byte nibble)
    {
        int step = ImaAdpcmTables.StepTable[_stepIndex];
        int diff = step >> 3;

        if ((nibble & 4) != 0) diff += step;
        if ((nibble & 2) != 0) diff += step >> 1;
        if ((nibble & 1) != 0) diff += step >> 2;

        if ((nibble & 8) != 0)
            _predictor -= diff;
        else
            _predictor += diff;

        _predictor = Math.Clamp(_predictor, -32768, 32767);
        _stepIndex = Math.Clamp(_stepIndex + ImaAdpcmTables.IndexTable[nibble & 7], 0, 88);

        return (short)_predictor;
    }
}
