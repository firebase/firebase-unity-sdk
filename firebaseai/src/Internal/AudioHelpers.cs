using System;

namespace Firebase.AI.Internal
{
  internal static class AudioHelpers
  {
    // Helper function to convert a byte array representing a 16-bit encoded
    // Audio snippet (raw PCM or RIFF WAVE) into a float array, which Unity's
    // built-in libraries support.
    public static float[] ConvertPcmBytesToFloat(byte[] byteArray)
    {
      if (byteArray == null || byteArray.Length < 2)
      {
        return Array.Empty<float>();
      }

      int pcmOffset = 0;
      int pcmLength = byteArray.Length;

      if (TryGetWavDataChunk(byteArray, out int dataOffset, out int dataLength))
      {
        pcmOffset = dataOffset;
        pcmLength = dataLength;
      }

      // Assumes 16 bit encoding, which would be two bytes per sample.
      int sampleCount = pcmLength / 2;
      float[] floatArray = new float[sampleCount];

      for (int i = 0; i < sampleCount; i++)
      {
        int byteIndex = pcmOffset + (i * 2);
        float sample = unchecked((short)(byteArray[byteIndex] | (byteArray[byteIndex + 1] << 8))) / 32768f;
        floatArray[i] = Math.Clamp(sample, -1f, 1f); // Ensure values are within the valid range
      }

      return floatArray;
    }

    // Attempts to parse a RIFF WAVE container to find the "data" subchunk's
    // starting byte offset and byte length, stripping both the leading header
    // and any trailing metadata chunks.
    private static bool TryGetWavDataChunk(byte[] byteArray, out int dataOffset, out int dataLength)
    {
      dataOffset = 0;
      dataLength = 0;

      // A valid RIFF WAVE header requires at least 12 bytes for "RIFF" + size + "WAVE"
      // plus at least 8 bytes for a subchunk header.
      if (byteArray == null || byteArray.Length < 20)
      {
        return false;
      }

      // Check for "RIFF" (0x52, 0x49, 0x46, 0x46) at offset 0
      // and "WAVE" (0x57, 0x41, 0x56, 0x45) at offset 8.
      if (byteArray[0] != 'R' || byteArray[1] != 'I' || byteArray[2] != 'F' || byteArray[3] != 'F' ||
          byteArray[8] != 'W' || byteArray[9] != 'A' || byteArray[10] != 'V' || byteArray[11] != 'E')
      {
        return false;
      }

      int offset = 12;
      while (offset + 8 <= byteArray.Length)
      {
        uint chunkSize = (uint)(byteArray[offset + 4] |
                                (byteArray[offset + 5] << 8) |
                                (byteArray[offset + 6] << 16) |
                                (byteArray[offset + 7] << 24));

        // Check for "data" (0x64, 0x61, 0x74, 0x61) subchunk ID.
        if (byteArray[offset] == 'd' &&
            byteArray[offset + 1] == 'a' &&
            byteArray[offset + 2] == 't' &&
            byteArray[offset + 3] == 'a')
        {
          dataOffset = offset + 8;
          int maxAvailable = byteArray.Length - dataOffset;
          if (chunkSize == 0xFFFFFFFFu || chunkSize > (uint)maxAvailable)
          {
            dataLength = maxAvailable;
          }
          else
          {
            dataLength = (int)chunkSize;
          }
          return true;
        }

        // Advance to the next subchunk (RIFF chunks are word-aligned to even byte boundaries).
        long nextOffset = (long)offset + 8 + chunkSize + (chunkSize & 1);
        if (nextOffset > byteArray.Length || nextOffset <= offset)
        {
          break;
        }
        offset = (int)nextOffset;
      }

      return false;
    }
  }
}
