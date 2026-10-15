using Unity.Collections.LowLevel.Unsafe;

namespace ECSave.Internal
{
    internal static unsafe class Lz4Block
    {
        private const int HashBits = 12;
        private const int MinMatch = 4;
        private const int MfLimit = 12;
        private const int LastLiterals = 5;
        private const int MaxOffset = 65535;

        public static int MaxCompressedSize(int n) => n + n / 255 + 16;

        public static int Compress(byte* src, int srcLen, byte* dst, int dstCap)
        {
            if (dstCap < MaxCompressedSize(srcLen)) return -1;

            byte* ip = src;
            byte* anchor = src;
            byte* op = dst;
            byte* iend = src + srcLen;
            byte* mflimit = iend - MfLimit;
            byte* matchLimit = iend - LastLiterals;

            if (srcLen >= MfLimit + 1)
            {
                int* table = stackalloc int[1 << HashBits];
                for (int i = 0; i < (1 << HashBits); i++) table[i] = -1;

                table[Hash(Read32(ip))] = 0;
                ip++;

                while (true)
                {
                    byte* match;
                    while (true)
                    {
                        if (ip >= mflimit) goto Last;
                        uint seq = Read32(ip);
                        int h = Hash(seq);
                        int cand = table[h];
                        int pos = (int)(ip - src);
                        table[h] = pos;
                        if (cand >= 0 && pos - cand <= MaxOffset && Read32(src + cand) == seq)
                        {
                            match = src + cand;
                            break;
                        }
                        ip++;
                    }

                    while (ip > anchor && match > src && ip[-1] == match[-1]) { ip--; match--; }

                    byte* p = ip + MinMatch;
                    byte* mp = match + MinMatch;
                    while (p < matchLimit && *p == *mp) { p++; mp++; }
                    int matchLen = (int)(p - ip);
                    int litLen = (int)(ip - anchor);

                    byte* token = op++;
                    op = WriteLiteralLength(token, op, litLen);
                    UnsafeUtility.MemCpy(op, anchor, litLen);
                    op += litLen;

                    int offset = (int)(ip - match);
                    *op++ = (byte)offset;
                    *op++ = (byte)(offset >> 8);

                    int ml = matchLen - MinMatch;
                    if (ml >= 15)
                    {
                        *token |= 15;
                        ml -= 15;
                        while (ml >= 255) { *op++ = 255; ml -= 255; }
                        *op++ = (byte)ml;
                    }
                    else
                    {
                        *token |= (byte)ml;
                    }

                    ip += matchLen;
                    anchor = ip;
                    if (ip < mflimit)
                        table[Hash(Read32(ip - 2))] = (int)(ip - 2 - src);
                }
            }

        Last:
            {
                int litLen = (int)(iend - anchor);
                byte* token = op++;
                op = WriteLiteralLength(token, op, litLen);
                UnsafeUtility.MemCpy(op, anchor, litLen);
                op += litLen;
                return (int)(op - dst);
            }
        }

        public static bool Decompress(byte* src, int srcLen, byte* dst, int dstLen)
        {
            byte* ip = src;
            byte* iend = src + srcLen;
            byte* op = dst;
            byte* oend = dst + dstLen;

            while (true)
            {
                if (ip >= iend) return false;
                int token = *ip++;

                int litLen = token >> 4;
                if (litLen == 15)
                {
                    int b;
                    do
                    {
                        if (ip >= iend) return false;
                        b = *ip++;
                        litLen += b;
                        if (litLen > dstLen) return false;
                    } while (b == 255);
                }
                if (litLen > iend - ip || litLen > oend - op) return false;
                UnsafeUtility.MemCpy(op, ip, litLen);
                op += litLen;
                ip += litLen;

                if (ip == iend) return op == oend;

                if (iend - ip < 2) return false;
                int offset = ip[0] | (ip[1] << 8);
                ip += 2;
                if (offset == 0 || offset > op - dst) return false;

                int ml = token & 15;
                if (ml == 15)
                {
                    int b;
                    do
                    {
                        if (ip >= iend) return false;
                        b = *ip++;
                        ml += b;
                        if (ml > dstLen) return false;
                    } while (b == 255);
                }
                ml += MinMatch;
                if (ml > oend - op) return false;

                byte* mp = op - offset;
                if (offset >= ml)
                {
                    UnsafeUtility.MemCpy(op, mp, ml);
                }
                else
                {
                    for (int i = 0; i < ml; i++) op[i] = mp[i];
                }
                op += ml;
            }
        }

        private static uint Read32(byte* p) => *(uint*)p;

        private static int Hash(uint seq)
            => (int)(unchecked(seq * 2654435761u) >> (32 - HashBits));

        private static byte* WriteLiteralLength(byte* token, byte* op, int len)
        {
            if (len >= 15)
            {
                *token = 15 << 4;
                len -= 15;
                while (len >= 255) { *op++ = 255; len -= 255; }
                *op++ = (byte)len;
            }
            else
            {
                *token = (byte)(len << 4);
            }
            return op;
        }
    }
}
