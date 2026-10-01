using System;
using System.Numerics;

namespace FloorplansRevitExtractor.Core.Label
{
    /// <summary>
    /// Python's round(): half to even, on the exact binary value of the
    /// double. The label fields derived from pixels mirror the generator's
    /// Python, so they must round the same way. Math.Round(value, digits)
    /// scales by 10^digits first and loses the tail: 137.65 is really
    /// 137.65000000000000568..., which Python rounds to 137.7 but
    /// Math.Round turns into a false tie and rounds to 137.6.
    /// </summary>
    public static class PythonRounding
    {
        /// <summary>Doubles above this scaled magnitude are already integers at any digit count we use.</summary>
        private const int MaxDigits = 15;

        public static double Round(double value, int digits)
        {
            if (digits < 0 || digits > MaxDigits)
            {
                throw new ArgumentOutOfRangeException(nameof(digits), digits, $"Digits must be between 0 and {MaxDigits}.");
            }

            if (double.IsNaN(value) || double.IsInfinity(value) || value == 0)
            {
                return value;
            }

            (BigInteger mantissa, int exponent) = Decompose(Math.Abs(value));
            BigInteger scale = BigInteger.Pow(10, digits);
            BigInteger numerator = exponent >= 0 ? mantissa * scale * BigInteger.Pow(2, exponent) : mantissa * scale;
            BigInteger denominator = exponent >= 0 ? BigInteger.One : BigInteger.Pow(2, -exponent);

            BigInteger quotient = BigInteger.DivRem(numerator, denominator, out BigInteger remainder);
            int half = (remainder * 2).CompareTo(denominator);
            if (half > 0 || (half == 0 && !quotient.IsEven))
            {
                quotient += 1;
            }

            // Both operands are exact (quotient < 2^53, 10^digits <= 10^15), so the
            // division is correctly rounded, as Python's decimal-to-double step is.
            double rounded = (double)quotient / Math.Pow(10, digits);
            return value < 0 ? -rounded : rounded;
        }

        public static int RoundToInt(double value) => (int)Math.Round(value, MidpointRounding.ToEven);

        /// <summary>Splits a positive finite double into mantissa x 2^exponent, exactly.</summary>
        private static (BigInteger Mantissa, int Exponent) Decompose(double value)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            int biasedExponent = (int)((bits >> 52) & 0x7FF);
            long fraction = bits & 0xFFFFFFFFFFFFFL;
            return biasedExponent == 0
                ? (new BigInteger(fraction), -1074) // subnormal
                : (new BigInteger(fraction | (1L << 52)), biasedExponent - 1075);
        }
    }
}
