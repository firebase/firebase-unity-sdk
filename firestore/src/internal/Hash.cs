using System;

namespace Firebase.Firestore.Internal {
  internal static class Hash {
    internal static int LongHash(long l) {
      return (int)((l ^ l >> 32) & 0xFFFFFFFF);
    }

    internal static int DoubleBitwiseHash(double d) {
      return LongHash(BitConverter.DoubleToInt64Bits(d));
    }
  }
}
