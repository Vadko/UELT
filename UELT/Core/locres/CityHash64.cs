using uint32 = System.UInt32;
using uint64 = System.UInt64;
using uint8 = System.Byte;

namespace UELT.Core.locres
{
    public static class CityHash
    {
        private const uint64 k0 = 0xc3a5c85c97cb3127UL;
        private const uint64 k1 = 0xb492b66fbe98f273UL;
        private const uint64 k2 = 0x9ae16a3b2f90404fUL;
        private const uint32 c1 = 0xcc9e2d51;
        private const uint32 c2 = 0x1b873593;

        public static bool IsBigEndian { get; set; } = false;

        public struct Uint128_64
        {
            public ulong lo;
            public ulong hi;

            public Uint128_64(ulong inLo, ulong inHi)
            {
                lo = inLo;
                hi = inHi;
            }
        }

        private static ulong CityHash128to64(ref Uint128_64 x)
        {
            const ulong kMul = 0x9ddfea08eb382d69UL;
            ulong a = (x.lo ^ x.hi) * kMul;
            a ^= (a >> 47);
            ulong b = (x.hi ^ a) * kMul;
            b ^= (b >> 47);
            b *= kMul;
            return b;
        }

        private static unsafe uint64 UNALIGNED_LOAD64(byte* p) => *(uint64*)p;
        private static unsafe uint32 UNALIGNED_LOAD32(byte* p) => *(uint32*)p;

        private static uint32 _byteswap_ulong(uint32 value)
        {
            return ((value & 0xFFu) << 24) |
                   ((value & 0xFF00u) << 8) |
                   ((value & 0xFF0000u) >> 8) |
                   ((value >> 24) & 0xFFu);
        }

        private static uint64 _byteswap_uint64(uint64 value)
        {
            return ((value & 0xFFUL) << 56) |
                   ((value & 0xFF00UL) << 40) |
                   ((value & 0xFF0000UL) << 24) |
                   ((value & 0xFF000000UL) << 8) |
                   ((value & 0xFF00000000UL) >> 8) |
                   ((value & 0xFF0000000000UL) >> 24) |
                   ((value & 0xFF000000000000UL) >> 40) |
                   ((value >> 56) & 0xFFUL);
        }

        private static uint32 Bswap_32(uint32 x) => _byteswap_ulong(x);
        private static uint64 Bswap_64(uint64 x) => _byteswap_uint64(x);

        private static uint32 Uint32_in_expected_order(uint32 x) => Bswap_32(x);
        private static uint64 Uint64_in_expected_order(uint64 x) => Bswap_64(x);

        private static unsafe uint64 Fetch64(byte* p)
        {
            return IsBigEndian ? Uint64_in_expected_order(UNALIGNED_LOAD64(p)) : UNALIGNED_LOAD64(p);
        }

        private static unsafe uint32 Fetch32(byte* p)
        {
            return IsBigEndian ? Uint32_in_expected_order(UNALIGNED_LOAD32(p)) : UNALIGNED_LOAD32(p);
        }

        private static uint32 Fmix(uint32 h)
        {
            h ^= h >> 16;
            h *= 0x85ebca6b;
            h ^= h >> 13;
            h *= 0xc2b2ae35;
            h ^= h >> 16;
            return h;
        }

        private static uint32 Rotate32(uint32 val, int shift)
        {
            return shift == 0 ? val : ((val >> shift) | (val << (32 - shift)));
        }

        private static void SwapValues<T>(ref T a, ref T b)
        {
            T c = a;
            a = b;
            b = c;
        }

        private static uint32 Mur(uint32 a, uint32 h)
        {
            a *= c1;
            a = Rotate32(a, 17);
            a *= c2;
            h ^= a;
            h = Rotate32(h, 19);
            return h * 5 + 0xe6546b64;
        }

        private static unsafe uint32 Hash32Len13to24(byte* s, uint32 len)
        {
            uint32 a = Fetch32(s - 4 + (len >> 1));
            uint32 b = Fetch32(s + 4);
            uint32 c = Fetch32(s + len - 8);
            uint32 d = Fetch32(s + (len >> 1));
            uint32 e = Fetch32(s);
            uint32 f = Fetch32(s + len - 4);
            uint32 h = len;

            return Fmix(Mur(f, Mur(e, Mur(d, Mur(c, Mur(b, Mur(a, h)))))));
        }

        private static unsafe uint32 Hash32Len0to4(byte* s, uint32 len)
        {
            uint32 b = 0;
            uint32 c = 9;
            for (uint32 i = 0; i < len; i++)
            {
                byte v = s[i];
                b = b * c1 + v;
                c ^= b;
            }
            return Fmix(Mur(b, Mur(len, c)));
        }

        private static unsafe uint32 Hash32Len5to12(byte* s, uint32 len)
        {
            uint32 a = len, b = len * 5, c = 9, d = b;
            a += Fetch32(s);
            b += Fetch32(s + len - 4);
            c += Fetch32(s + ((len >> 1) & 4));
            return Fmix(Mur(c, Mur(b, Mur(a, d))));
        }

        public static unsafe uint32 CityHash32(byte* s, uint32 len)
        {
            if (len <= 24)
            {
                return len <= 12 ?
                    (len <= 4 ? Hash32Len0to4(s, len) : Hash32Len5to12(s, len)) :
                    Hash32Len13to24(s, len);
            }

            uint32 h = len, g = c1 * len, f = g;
            uint32 a0 = Rotate32(Fetch32(s + len - 4) * c1, 17) * c2;
            uint32 a1 = Rotate32(Fetch32(s + len - 8) * c1, 17) * c2;
            uint32 a2 = Rotate32(Fetch32(s + len - 16) * c1, 17) * c2;
            uint32 a3 = Rotate32(Fetch32(s + len - 12) * c1, 17) * c2;
            uint32 a4 = Rotate32(Fetch32(s + len - 20) * c1, 17) * c2;
            h ^= a0;
            h = Rotate32(h, 19) * 5 + 0xe6546b64;
            h ^= a2;
            h = Rotate32(h, 19) * 5 + 0xe6546b64;
            g ^= a1;
            g = Rotate32(g, 19) * 5 + 0xe6546b64;
            g ^= a3;
            g = Rotate32(g, 19) * 5 + 0xe6546b64;
            f += a4;
            f = Rotate32(f, 19) * 5 + 0xe6546b64;

            uint32 iters = (len - 1) / 20;
            do
            {
                uint32 _a0 = Rotate32(Fetch32(s) * c1, 17) * c2;
                uint32 _a1 = Fetch32(s + 4);
                uint32 _a2 = Rotate32(Fetch32(s + 8) * c1, 17) * c2;
                uint32 _a3 = Rotate32(Fetch32(s + 12) * c1, 17) * c2;
                uint32 _a4 = Fetch32(s + 16);
                h ^= _a0;
                h = Rotate32(h, 18) * 5 + 0xe6546b64;
                f += _a1;
                f = Rotate32(f, 19) * c1;
                g += _a2;
                g = Rotate32(g, 18) * 5 + 0xe6546b64;
                h ^= _a3 + _a1;
                h = Rotate32(h, 19) * 5 + 0xe6546b64;
                g ^= _a4;
                g = Bswap_32(g) * 5;
                h += _a4 * 5;
                h = Bswap_32(h);
                f += _a0;

                uint32 temp = f;
                f = g;
                g = h;
                h = temp;

                s += 20;
            } while (--iters != 0);

            g = Rotate32(g, 11) * c1;
            g = Rotate32(g, 17) * c1;
            f = Rotate32(f, 11) * c1;
            f = Rotate32(f, 17) * c1;
            h = Rotate32(h + g, 19) * 5 + 0xe6546b64;
            h = Rotate32(h, 17) * c1;
            h = Rotate32(h + f, 19) * 5 + 0xe6546b64;
            h = Rotate32(h, 17) * c1;
            return h;
        }

        private static uint64 Rotate(uint64 val, int shift) => shift == 0 ? val : ((val >> shift) | (val << (64 - shift)));
        private static uint64 ShiftMix(uint64 val) => val ^ (val >> 47);

        private static uint64 HashLen16(uint64 u, uint64 v)
        {
            var val = new Uint128_64(u, v);
            return CityHash128to64(ref val);
        }

        private static uint64 HashLen16(uint64 u, uint64 v, uint64 mul)
        {
            uint64 a = (u ^ v) * mul;
            a ^= (a >> 47);
            uint64 b = (v ^ a) * mul;
            b ^= (b >> 47);
            b *= mul;
            return b;
        }

        private static unsafe uint64 HashLen0to16(byte* s, uint32 len)
        {
            if (len >= 8)
            {
                uint64 mul = k2 + len * 2;
                uint64 a = Fetch64(s) + k2;
                uint64 b = Fetch64(s + len - 8);
                uint64 c = Rotate(b, 37) * mul + a;
                uint64 d = (Rotate(a, 25) + b) * mul;
                return HashLen16(c, d, mul);
            }
            if (len >= 4)
            {
                uint64 mul = k2 + len * 2;
                uint64 a = Fetch32(s);
                return HashLen16(len + (a << 3), Fetch32(s + len - 4), mul);
            }
            if (len > 0)
            {
                uint8 a = s[0];
                uint8 b = s[len >> 1];
                uint8 c = s[len - 1];
                uint32 y = a + ((uint32)b << 8);
                uint32 z = len + ((uint32)c << 2);
                return ShiftMix(y * k2 ^ z * k0) * k2;
            }
            return k2;
        }

        private static unsafe uint64 HashLen17to32(byte* s, uint32 len)
        {
            uint64 mul = k2 + len * 2;
            uint64 a = Fetch64(s) * k1;
            uint64 b = Fetch64(s + 8);
            uint64 c = Fetch64(s + len - 8) * mul;
            uint64 d = Fetch64(s + len - 16) * k2;
            return HashLen16(Rotate(a + b, 43) + Rotate(c, 30) + d,
                a + Rotate(b + k2, 18) + c, mul);
        }

        private static Uint128_64 WeakHashLen32WithSeeds(uint64 w, uint64 x, uint64 y, uint64 z, uint64 a, uint64 b)
        {
            a += w;
            b = Rotate(b + a + z, 21);
            uint64 c = a;
            a += x;
            a += y;
            b += Rotate(a, 44);
            return new Uint128_64(a + z, b + c);
        }

        private static unsafe Uint128_64 WeakHashLen32WithSeeds(byte* s, uint64 a, uint64 b)
        {
            return WeakHashLen32WithSeeds(Fetch64(s),
                Fetch64(s + 8),
                Fetch64(s + 16),
                Fetch64(s + 24),
                a,
                b);
        }

        private static unsafe uint64 HashLen33to64(byte* s, uint32 len)
        {
            uint64 mul = k2 + len * 2;
            uint64 a = Fetch64(s) * k2;
            uint64 b = Fetch64(s + 8);
            uint64 c = Fetch64(s + len - 24);
            uint64 d = Fetch64(s + len - 32);
            uint64 e = Fetch64(s + 16) * k2;
            uint64 f = Fetch64(s + 24) * 9;
            uint64 g = Fetch64(s + len - 8);
            uint64 h = Fetch64(s + len - 16) * mul;
            uint64 u = Rotate(a + g, 43) + (Rotate(b, 30) + c) * 9;
            uint64 v = ((a + g) ^ d) + f + 1;
            uint64 w = Bswap_64((u + v) * mul) + h;
            uint64 x = Rotate(e + f, 42) + c;
            uint64 y = (Bswap_64((v + w) * mul) + g) * mul;
            uint64 z = e + f + c;
            a = Bswap_64((x + z) * mul + y) + b;
            b = ShiftMix((z + a) * mul + d + h) * mul;
            return b + x;
        }

        public static unsafe uint64 CityHash64(byte* s, uint32 len)
        {
            if (len <= 32)
            {
                return len <= 16 ? HashLen0to16(s, len) : HashLen17to32(s, len);
            }

            if (len <= 64)
            {
                return HashLen33to64(s, len);
            }

            uint64 x = Fetch64(s + len - 40);
            uint64 y = Fetch64(s + len - 16) + Fetch64(s + len - 56);
            uint64 z = HashLen16(Fetch64(s + len - 48) + len, Fetch64(s + len - 24));
            Uint128_64 v = WeakHashLen32WithSeeds(s + len - 64, len, z);
            Uint128_64 w = WeakHashLen32WithSeeds(s + len - 32, y + k1, x);
            x = x * k1 + Fetch64(s);

            len = (len - 1) & ~(uint32)63;
            do
            {
                x = Rotate(x + y + v.lo + Fetch64(s + 8), 37) * k1;
                y = Rotate(y + v.hi + Fetch64(s + 48), 42) * k1;
                x ^= w.hi;
                y += v.lo + Fetch64(s + 40);
                z = Rotate(z + w.lo, 33) * k1;
                v = WeakHashLen32WithSeeds(s, v.hi * k1, x + w.lo);
                w = WeakHashLen32WithSeeds(s + 32, z + w.hi, y + Fetch64(s + 16));

                uint64 tempZ = z;
                z = x;
                x = tempZ;

                s += 64;
                len -= 64;
            } while (len != 0);

            return HashLen16(HashLen16(v.lo, w.lo) + ShiftMix(y) * k1 + z,
                HashLen16(v.hi, w.hi) + x);
        }

        public static ulong CityHash64(byte[] s)
        {
            if (s == null) return 0;
            unsafe
            {
                fixed (byte* ptr = s)
                {
                    return CityHash64(ptr, (uint)s.Length);
                }
            }
        }
    }
}