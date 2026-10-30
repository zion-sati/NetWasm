// Managed port of musl's exp.c, exp2.c and exp_data.c, Arm's optimized
// routines; the special case is pow.c's, which also covers a negative scale. Upstream: musl v1.2.5, commit 0784374d561435f7c787a555aeab8ede699ed298.
// Adaptations: C# bit access, hexadecimal constants written as their exact
// decimal values, the shared 2^(k/N) table as constant data read without
// bounds checks at the masked index, and the overflow and underflow
// helpers as the products they return.
/*
 * Double-precision e^x and 2^x functions and their shared data.
 *
 * Copyright (c) 2018, Arm Limited.
 * SPDX-License-Identifier: MIT
 */

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System;

public static partial class Math
{
    private const int ExpTableBits = 7;
    private const ulong ExpTableMask = (1 << ExpTableBits) - 1;
    // Shared by exp and pow.
    private const double ExpInvLn2N = 184.6649652337873; // 0x1.71547652b82fep0 * N
    private const double ExpShift = 6755399441055744.0; // 0x1.8p52
    private const double ExpNegLn2HiN = -0.005415212348111709; // -0x1.62e42fefa0000p-8
    private const double ExpNegLn2LoN = -1.2864023111638346e-14; // -0x1.cf79abc9e3b3ap-47
    private const double ExpC2 = 0.49999999999996786; // 0x1.ffffffffffdbdp-2
    private const double ExpC3 = 0.16666666666665886; // 0x1.555555555543cp-3
    private const double ExpC4 = 0.0416666808410674; // 0x1.55555cf172b91p-5
    private const double ExpC5 = 0.008333335853059549; // 0x1.1111167a4d017p-7

    // 2^(k/N) ~= H[k]*(1 + T[k]) for int k in [0,N)
    // ExpTable[2*k] = asuint64(T[k])
    // ExpTable[2*k+1] = asuint64(H[k]) - (k << 52)/N
    private static ReadOnlySpan<ulong> ExpTable =>
    [
        0x0000000000000000, 0x3FF0000000000000,
        0x3C9B3B4F1A88BF6E, 0x3FEFF63DA9FB3335,
        0xBC7160139CD8DC5D, 0x3FEFEC9A3E778061,
        0xBC905E7A108766D1, 0x3FEFE315E86E7F85,
        0x3C8CD2523567F613, 0x3FEFD9B0D3158574,
        0xBC8BCE8023F98EFA, 0x3FEFD06B29DDF6DE,
        0x3C60F74E61E6C861, 0x3FEFC74518759BC8,
        0x3C90A3E45B33D399, 0x3FEFBE3ECAC6F383,
        0x3C979AA65D837B6D, 0x3FEFB5586CF9890F,
        0x3C8EB51A92FDEFFC, 0x3FEFAC922B7247F7,
        0x3C3EBE3D702F9CD1, 0x3FEFA3EC32D3D1A2,
        0xBC6A033489906E0B, 0x3FEF9B66AFFED31B,
        0xBC9556522A2FBD0E, 0x3FEF9301D0125B51,
        0xBC5080EF8C4EEA55, 0x3FEF8ABDC06C31CC,
        0xBC91C923B9D5F416, 0x3FEF829AAEA92DE0,
        0x3C80D3E3E95C55AF, 0x3FEF7A98C8A58E51,
        0xBC801B15EAA59348, 0x3FEF72B83C7D517B,
        0xBC8F1FF055DE323D, 0x3FEF6AF9388C8DEA,
        0x3C8B898C3F1353BF, 0x3FEF635BEB6FCB75,
        0xBC96D99C7611EB26, 0x3FEF5BE084045CD4,
        0x3C9AECF73E3A2F60, 0x3FEF54873168B9AA,
        0xBC8FE782CB86389D, 0x3FEF4D5022FCD91D,
        0x3C8A6F4144A6C38D, 0x3FEF463B88628CD6,
        0x3C807A05B0E4047D, 0x3FEF3F49917DDC96,
        0x3C968EFDE3A8A894, 0x3FEF387A6E756238,
        0x3C875E18F274487D, 0x3FEF31CE4FB2A63F,
        0x3C80472B981FE7F2, 0x3FEF2B4565E27CDD,
        0xBC96B87B3F71085E, 0x3FEF24DFE1F56381,
        0x3C82F7E16D09AB31, 0x3FEF1E9DF51FDEE1,
        0xBC3D219B1A6FBFFA, 0x3FEF187FD0DAD990,
        0x3C8B3782720C0AB4, 0x3FEF1285A6E4030B,
        0x3C6E149289CECB8F, 0x3FEF0CAFA93E2F56,
        0x3C834D754DB0ABB6, 0x3FEF06FE0A31B715,
        0x3C864201E2AC744C, 0x3FEF0170FC4CD831,
        0x3C8FDD395DD3F84A, 0x3FEEFC08B26416FF,
        0xBC86A3803B8E5B04, 0x3FEEF6C55F929FF1,
        0xBC924AEDCC4B5068, 0x3FEEF1A7373AA9CB,
        0xBC9907F81B512D8E, 0x3FEEECAE6D05D866,
        0xBC71D1E83E9436D2, 0x3FEEE7DB34E59FF7,
        0xBC991919B3CE1B15, 0x3FEEE32DC313A8E5,
        0x3C859F48A72A4C6D, 0x3FEEDEA64C123422,
        0xBC9312607A28698A, 0x3FEEDA4504AC801C,
        0xBC58A78F4817895B, 0x3FEED60A21F72E2A,
        0xBC7C2C9B67499A1B, 0x3FEED1F5D950A897,
        0x3C4363ED60C2AC11, 0x3FEECE086061892D,
        0x3C9666093B0664EF, 0x3FEECA41ED1D0057,
        0x3C6ECCE1DAA10379, 0x3FEEC6A2B5C13CD0,
        0x3C93FF8E3F0F1230, 0x3FEEC32AF0D7D3DE,
        0x3C7690CEBB7AAFB0, 0x3FEEBFDAD5362A27,
        0x3C931DBDEB54E077, 0x3FEEBCB299FDDD0D,
        0xBC8F94340071A38E, 0x3FEEB9B2769D2CA7,
        0xBC87DECCDC93A349, 0x3FEEB6DAA2CF6642,
        0xBC78DEC6BD0F385F, 0x3FEEB42B569D4F82,
        0xBC861246EC7B5CF6, 0x3FEEB1A4CA5D920F,
        0x3C93350518FDD78E, 0x3FEEAF4736B527DA,
        0x3C7B98B72F8A9B05, 0x3FEEAD12D497C7FD,
        0x3C9063E1E21C5409, 0x3FEEAB07DD485429,
        0x3C34C7855019C6EA, 0x3FEEA9268A5946B7,
        0x3C9432E62B64C035, 0x3FEEA76F15AD2148,
        0xBC8CE44A6199769F, 0x3FEEA5E1B976DC09,
        0xBC8C33C53BEF4DA8, 0x3FEEA47EB03A5585,
        0xBC845378892BE9AE, 0x3FEEA34634CCC320,
        0xBC93CEDD78565858, 0x3FEEA23882552225,
        0x3C5710AA807E1964, 0x3FEEA155D44CA973,
        0xBC93B3EFBF5E2228, 0x3FEEA09E667F3BCD,
        0xBC6A12AD8734B982, 0x3FEEA012750BDABF,
        0xBC6367EFB86DA9EE, 0x3FEE9FB23C651A2F,
        0xBC80DC3D54E08851, 0x3FEE9F7DF9519484,
        0xBC781F647E5A3ECF, 0x3FEE9F75E8EC5F74,
        0xBC86EE4AC08B7DB0, 0x3FEE9F9A48A58174,
        0xBC8619321E55E68A, 0x3FEE9FEB564267C9,
        0x3C909CCB5E09D4D3, 0x3FEEA0694FDE5D3F,
        0xBC7B32DCB94DA51D, 0x3FEEA11473EB0187,
        0x3C94ECFD5467C06B, 0x3FEEA1ED0130C132,
        0x3C65EBE1ABD66C55, 0x3FEEA2F336CF4E62,
        0xBC88A1C52FB3CF42, 0x3FEEA427543E1A12,
        0xBC9369B6F13B3734, 0x3FEEA589994CCE13,
        0xBC805E843A19FF1E, 0x3FEEA71A4623C7AD,
        0xBC94D450D872576E, 0x3FEEA8D99B4492ED,
        0x3C90AD675B0E8A00, 0x3FEEAAC7D98A6699,
        0x3C8DB72FC1F0EAB4, 0x3FEEACE5422AA0DB,
        0xBC65B6609CC5E7FF, 0x3FEEAF3216B5448C,
        0x3C7BF68359F35F44, 0x3FEEB1AE99157736,
        0xBC93091FA71E3D83, 0x3FEEB45B0B91FFC6,
        0xBC5DA9B88B6C1E29, 0x3FEEB737B0CDC5E5,
        0xBC6C23F97C90B959, 0x3FEEBA44CBC8520F,
        0xBC92434322F4F9AA, 0x3FEEBD829FDE4E50,
        0xBC85CA6CD7668E4B, 0x3FEEC0F170CA07BA,
        0x3C71AFFC2B91CE27, 0x3FEEC49182A3F090,
        0x3C6DD235E10A73BB, 0x3FEEC86319E32323,
        0xBC87C50422622263, 0x3FEECC667B5DE565,
        0x3C8B1C86E3E231D5, 0x3FEED09BEC4A2D33,
        0xBC91BBD1D3BCBB15, 0x3FEED503B23E255D,
        0x3C90CC319CEE31D2, 0x3FEED99E1330B358,
        0x3C8469846E735AB3, 0x3FEEDE6B5579FDBF,
        0xBC82DFCD978E9DB4, 0x3FEEE36BBFD3F37A,
        0x3C8C1A7792CB3387, 0x3FEEE89F995AD3AD,
        0xBC907B8F4AD1D9FA, 0x3FEEEE07298DB666,
        0xBC55C3D956DCAEBA, 0x3FEEF3A2B84F15FB,
        0xBC90A40E3DA6F640, 0x3FEEF9728DE5593A,
        0xBC68D6F438AD9334, 0x3FEEFF76F2FB5E47,
        0xBC91EEE26B588A35, 0x3FEF05B030A1064A,
        0x3C74FFD70A5FDDCD, 0x3FEF0C1E904BC1D2,
        0xBC91BDFBFA9298AC, 0x3FEF12C25BD71E09,
        0x3C736EAE30AF0CB3, 0x3FEF199BDD85529C,
        0x3C8EE3325C9FFD94, 0x3FEF20AB5FFFD07A,
        0x3C84E08FD10959AC, 0x3FEF27F12E57D14B,
        0x3C63CDAF384E1A67, 0x3FEF2F6D9406E7B5,
        0x3C676B2C6C921968, 0x3FEF3720DCEF9069,
        0xBC808A1883CCB5D2, 0x3FEF3F0B555DC3FA,
        0xBC8FAD5D3FFFFA6F, 0x3FEF472D4A07897C,
        0xBC900DAE3875A949, 0x3FEF4F87080D89F2,
        0x3C74A385A63D07A7, 0x3FEF5818DCFBA487,
        0xBC82919E2040220F, 0x3FEF60E316C98398,
        0x3C8E5A50D5C192AC, 0x3FEF69E603DB3285,
        0x3C843A59AC016B4B, 0x3FEF7321F301B460,
        0xBC82D52107B43E1F, 0x3FEF7C97337B9B5F,
        0xBC892AB93B470DC9, 0x3FEF864614F5A129,
        0x3C74B604603A88D3, 0x3FEF902EE78B3FF6,
        0x3C83C5EC519D7271, 0x3FEF9A51FBC74C83,
        0xBC8FF7128FD391F0, 0x3FEFA4AFA2A490DA,
        0xBC8DAE98E223747D, 0x3FEFAF482D8E67F1,
        0x3C8EC3BC41AA2008, 0x3FEFBA1BEE615A27,
        0x3C842B94C3A9EB32, 0x3FEFC52B376BBA97,
        0x3C8A64A931D185EE, 0x3FEFD0765B6E4540,
        0xBC8E37BAE43BE3ED, 0x3FEFDBFDAD9CBE14,
        0x3C77893B4D91CD9D, 0x3FEFE7C1819E90D8,
        0x3C5305C14160CC89, 0x3FEFF3C22B8F71F1,
    ];

    public static double Exp(double value)
    {

        var x = value;
        uint abstop;
        ulong ki, idx, top, sbits;
        double kd, z, r, r2, scale, tail, tmp;

        abstop = Top12(x) & 0x7ff;
        // NetWasm: below 2^-28, as fdlibm's exp does, rather than 2^-54. There
        // x*x/2 is under a sixteenth of an ulp of 1, and 1 + x stays within
        // one ulp without the table and polynomial.
        if (abstop - 0x3e3u >= 0x408u - 0x3e3u)
        {   /* |x| < 0x1p-28 or |x| >= 512 */
            if (abstop - 0x3e3u >= 0x80000000)
                /* Avoid spurious underflow for tiny x.  */
                /* Note: 0 is common input.  */
                return 1.0 + x;
            if (abstop >= 0x409)
            {   /* |x| >= 1024 */
                if (BitConverter.DoubleToUInt64Bits(x) == 0xfff0000000000000)
                    return 0.0;
                if (abstop >= 0x7ff)
                    return 1.0 + x;
                if ((BitConverter.DoubleToUInt64Bits(x) >> 63) != 0)
                    return ExpUnderflow(false);
                else
                    return ExpOverflow(false);
            }
            /* Large x is special cased below.  */
            abstop = 0;
        }

        /* exp(x) = 2^(k/N) * exp(r), with exp(r) in [2^(-1/2N),2^(1/2N)].  */
        /* x = ln2/N*k + r, with int k and r in [-ln2/2N, ln2/2N].  */
        z = ExpInvLn2N * x;
        /* z - kd is in [-1, 1] in non-nearest rounding modes.  */
        kd = z + ExpShift;
        ki = BitConverter.DoubleToUInt64Bits(kd);
        kd -= ExpShift;
        r = x + kd * ExpNegLn2HiN + kd * ExpNegLn2LoN;
        /* 2^(k/N) ~= scale * (1 + tail).  */
        idx = 2 * (ki & ExpTableMask);
        top = ki << (52 - ExpTableBits);
        /* idx is below the table's 2*N entries.  */
        ref var entry = ref Unsafe.Add(ref MemoryMarshal.GetReference(ExpTable), (nint)idx);
        tail = BitConverter.UInt64BitsToDouble(entry);
        /* This is only a valid scale when -1023*N < k < 1024*N.  */
        sbits = Unsafe.Add(ref entry, 1) + top;
        /* exp(x) = 2^(k/N) * exp(r) ~= scale + scale * (tail + exp(r) - 1).  */
        /* Evaluation is optimized assuming superscalar pipelined execution.  */
        r2 = r * r;
        /* Without fma the worst case error is 0.25/N ulp larger.  */
        /* Worst case error is less than 0.5+1.11/N+(abs poly error * 2^53) ulp.  */
        tmp = tail + r + r2 * (ExpC2 + r * ExpC3) + r2 * r2 * (ExpC4 + r * ExpC5);
        if (abstop == 0)
            return ExpSpecialCase(tmp, sbits, ki, 1009, 5.486124068793689e+303 /* 0x1p1009 */);
        scale = BitConverter.UInt64BitsToDouble(sbits);
        /* Note: tmp == 0 or |tmp| > 2^-200 and scale > 2^-739, so there
           is no spurious underflow here even without fma.  */
        return scale + scale * tmp;
    }

    internal static double Exp2(double value)
    {
        const double shift = 52776558133248.0; // 0x1.8p52 / N
        const double C1 = 0.6931471805599453; // 0x1.62e42fefa39efp-1
        const double C2 = 0.24022650695909065; // 0x1.ebfbdff82c424p-3
        const double C3 = 0.0555041086686087; // 0x1.c6b08d70cf4b5p-5
        const double C4 = 0.009618131975721055; // 0x1.3b2abd24650ccp-7
        const double C5 = 0.0013332074570119598; // 0x1.5d7e09b4e3a84p-10

        var x = value;
        uint abstop;
        ulong ki, idx, top, sbits;
        double kd, r, r2, scale, tail, tmp;

        abstop = Top12(x) & 0x7ff;
        if (abstop - 0x3c9u >= 0x408u - 0x3c9u)
        {   /* |x| < 0x1p-54 or |x| >= 512 */
            if (abstop - 0x3c9u >= 0x80000000)
                /* Avoid spurious underflow for tiny x.  */
                /* Note: 0 is common input.  */
                return 1.0 + x;
            if (abstop >= 0x409)
            {   /* |x| >= 1024 */
                if (BitConverter.DoubleToUInt64Bits(x) == 0xfff0000000000000)
                    return 0.0;
                if (abstop >= 0x7ff)
                    return 1.0 + x;
                if ((BitConverter.DoubleToUInt64Bits(x) >> 63) == 0)
                    return ExpOverflow(false);
                else if (BitConverter.DoubleToUInt64Bits(x) >= 0xc090cc0000000000) /* -1075.0 */
                    return ExpUnderflow(false);
            }
            if (2 * BitConverter.DoubleToUInt64Bits(x) > 0x811a000000000000) /* 2 * asuint64(928.0) */
                /* Large x is special cased below.  */
                abstop = 0;
        }

        /* exp2(x) = 2^(k/N) * 2^r, with 2^r in [2^(-1/2N),2^(1/2N)].  */
        /* x = k/N + r, with int k and r in [-1/2N, 1/2N].  */
        kd = x + shift;
        ki = BitConverter.DoubleToUInt64Bits(kd); /* k.  */
        kd -= shift; /* k/N for int k.  */
        r = x - kd;
        /* 2^(k/N) ~= scale * (1 + tail).  */
        idx = 2 * (ki & ExpTableMask);
        top = ki << (52 - ExpTableBits);
        /* idx is below the table's 2*N entries.  */
        ref var entry = ref Unsafe.Add(ref MemoryMarshal.GetReference(ExpTable), (nint)idx);
        tail = BitConverter.UInt64BitsToDouble(entry);
        /* This is only a valid scale when -1023*N < k < 1024*N.  */
        sbits = Unsafe.Add(ref entry, 1) + top;
        /* exp2(x) = 2^(k/N) * 2^r ~= scale + scale * (tail + 2^r - 1).  */
        /* Evaluation is optimized assuming superscalar pipelined execution.  */
        r2 = r * r;
        /* Without fma the worst case error is 0.5/N ulp larger.  */
        /* Worst case error is less than 0.5+0.86/N+(abs poly error * 2^53) ulp.  */
        tmp = tail + r * C1 + r2 * (C2 + r * C3) + r2 * r2 * (C4 + r * C5);
        if (abstop == 0)
            return ExpSpecialCase(tmp, sbits, ki, 1, 2.0);
        scale = BitConverter.UInt64BitsToDouble(sbits);
        /* Note: tmp == 0 or |tmp| > 2^-65 and scale > 2^-928, so there
           is no spurious underflow here even without fma.  */
        return scale + scale * tmp;
    }

    /* Top 12 bits of a double (sign and exponent bits).  */
    private static uint Top12(double x) => (uint)(BitConverter.DoubleToUInt64Bits(x) >> 52);

    // __math_oflow(sign) and __math_uflow(sign): +-0x1p769 * 0x1p769 and
    // +-0x1p-767 * 0x1p-767.
    private static double ExpOverflow(bool negative) =>
        (negative ? -3.105036184601418e+231 : 3.105036184601418e+231) * 3.105036184601418e+231;
    private static double ExpUnderflow(bool negative) =>
        (negative ? -1.2882297539194267e-231 : 1.2882297539194267e-231) * 1.2882297539194267e-231;

    /* Handle cases that may overflow or underflow when computing the result that
       is scale*(1+TMP) without intermediate rounding.  The bit representation of
       scale is in SBITS, however it has a computed exponent that may have
       overflown into the sign bit so that needs to be adjusted before using it as
       a double.  (int32_t)KI is the k used in the argument reduction and exponent
       adjustment of scale, positive k here means the result may overflow and
       negative k means the result may underflow.  This is pow's form, whose
       scale carries the result's sign; exp and pow pass 1009 and 0x1p1009,
       exp2 1 and 2, for the bias removed from an overflowing scale.  */
    private static double ExpSpecialCase(double tmp, ulong sbits, ulong ki, int overflowBias, double overflowScale)
    {
        double scale, y;

        if ((ki & 0x80000000) == 0)
        {
            /* k > 0, the exponent of scale might have overflowed.  */
            sbits -= (ulong)overflowBias << 52;
            scale = BitConverter.UInt64BitsToDouble(sbits);
            y = overflowScale * (scale + scale * tmp);
            return y;
        }
        /* k < 0, need special care in the subnormal range.  */
        sbits += 1022UL << 52;
        /* Note: sbits is signed scale.  */
        scale = BitConverter.UInt64BitsToDouble(sbits);
        y = scale + scale * tmp;
        if (Abs(y) < 1.0)
        {
            /* Round y to the right precision before scaling it into the subnormal
               range to avoid double rounding that can cause 0.5+E/2 ulp error where
               E is the worst-case ulp error outside the subnormal range.  So this
               is only useful if the goal is better than 1 ulp worst-case error.  */
            double hi, lo, one = 1.0;
            if (y < 0.0)
                one = -1.0;
            lo = scale - y + scale * tmp;
            hi = one + y;
            lo = one - hi + y + lo;
            y = (hi + lo) - one;
            /* Fix the sign of 0.  */
            if (y == 0.0)
                y = BitConverter.UInt64BitsToDouble(sbits & 0x8000000000000000);
        }
        y = 2.2250738585072014e-308 /* 0x1p-1022 */ * y;
        return y;
    }
}
