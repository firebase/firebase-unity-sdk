using System;

namespace Firebase.AI.Internal
{
  internal static class AudioHelpers
  {
    // Helper function to convert a byte array representing a 16-bit encoded
    // Audio snippet into a float array, which Unity's built in libraries supports.
    public static float[] ConvertPcmBytesToFloat(byte[] byteArray)
    {
      if (byteArray == null)
      {
        return Array.Empty<float>();
      }

      // Assumes 16 bit encoding, which would be two bytes per sample.
      int sampleCount = byteArray.Length / 2;
      float[] floatArray = new float[sampleCount];

      for (int i = 0; i < sampleCount; i++)
      {
        float sample = unchecked((short)(byteArray[i * 2] | (byteArray[i * 2 + 1] << 8))) / 32768f;
        floatArray[i] = Math.Clamp(sample, -1f, 1f); // Ensure values are within the valid range
      }

      return floatArray;
    }
  }
}
