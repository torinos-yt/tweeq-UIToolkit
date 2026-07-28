using System;

namespace Tweeq.Core
{
    #region Data

    /// <summary>
    /// OKLCH 色。<see cref="H"/> は無彩色のとき NaN。
    /// </summary>
    /// <remarks>
    /// NaN 色相は原典（colorjs.io）の表現をそのまま引き継いでいる。単なる「未定義」の印ではなく、
    /// radix.ts の getButtonHoverColor が `!isNaN(H)` で彩度を落とすかどうかを分岐させる
    /// 判定材料になっているので、0 に潰すとスケールの生成結果が変わる。
    /// </remarks>
    public struct Oklch
    {
        /// <summary>明度。[0, 1]。</summary>
        public double L;

        /// <summary>彩度。0〜0.4 程度。</summary>
        public double C;

        /// <summary>色相（度）。無彩色は NaN。</summary>
        public double H;

        public Oklch(double l, double c, double h)
        {
            L = l;
            C = c;
            H = h;
        }
    }

    /// <summary>OKLab 色（直交座標）。</summary>
    public struct Oklab
    {
        public double L;

        public double A;

        public double B;

        public Oklab(double l, double a, double b)
        {
            L = l;
            A = a;
            B = b;
        }
    }

    /// <summary>0〜255 に量子化された RGBA。Radix スケールの出力単位。</summary>
    public struct Rgba32
    {
        public int R;

        public int G;

        public int B;

        public int A;

        public Rgba32(int r, int g, int b, int a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        /// <summary>Core 共通の [0, 1] 表現へ。値は 1/255 の整数倍なので誤差は入らない。</summary>
        public Rgba ToRgba()
        {
            return new Rgba(R / 255.0, G / 255.0, B / 255.0, A / 255.0);
        }
    }

    #endregion

    /// <summary>
    /// sRGB / Display P3 ↔ OKLab ↔ OKLCH ↔ CIE Lab(D50) の変換と、Radix テーマ生成に必要な
    /// 付随計算（deltaEOK・APCA コントラスト・三次ベジェイージング）。純関数のみ・すべて double。
    /// </summary>
    /// <remarks>
    /// 行列・定数・分岐は colorjs.io 0.5.2 の実装に合わせてある（移植元 radix.ts が依存している版）。
    /// 「数学的に等価な別の式」に書き換えると最終出力が 1/255 単位でずれるので、式の形も維持する。
    /// </remarks>
    public static class TweeqOklch
    {
        #region Constants

        // Display P3 → XYZ(D65)
        static readonly double[] P3_TO_XYZ =
        {
            0.4865709486482162, 0.26566769316909306, 0.1982172852343625,
            0.2289745640697488, 0.6917385218365064, 0.079286914093745,
            0.0000000000000000, 0.04511338185890264, 1.043944368900976,
        };

        // sRGB → XYZ(D65)
        static readonly double[] SRGB_TO_XYZ =
        {
            0.41239079926595934, 0.357584339383878, 0.1804807884018343,
            0.21263900587151027, 0.715168678767756, 0.07219231536073371,
            0.01933081871559182, 0.11919477979462598, 0.9505321522496607,
        };

        // XYZ(D65) → sRGB
        static readonly double[] XYZ_TO_SRGB =
        {
            3.2409699419045226, -1.537383177570094, -0.4986107602930034,
            -0.9692436362808796, 1.8759675015077202, 0.04155505740717559,
            0.05563007969699366, -0.20397695888897652, 1.0569715142428786,
        };

        static readonly double[] XYZ_TO_LMS =
        {
            0.819022437996703, 0.3619062600528904, -0.1288737815209879,
            0.0329836539323885, 0.9292868615863434, 0.0361446663506424,
            0.0481771893596242, 0.2642395317527308, 0.6335478284694309,
        };

        static readonly double[] LMS_TO_XYZ =
        {
            1.2268798758459243, -0.5578149944602171, 0.2813910456659647,
            -0.0405757452148008, 1.112286803280317, -0.0717110580655164,
            -0.0763729366746601, -0.4214933324022432, 1.5869240198367816,
        };

        static readonly double[] LMS_TO_LAB =
        {
            0.210454268309314, 0.7936177747023054, -0.0040720430116193,
            1.9779985324311684, -2.42859224204858, 0.450593709617411,
            0.0259040424655478, 0.7827717124575296, -0.8086757549230774,
        };

        static readonly double[] LAB_TO_LMS =
        {
            1.0, 0.3963377773761749, 0.2158037573099136,
            1.0, -0.1055613458156586, -0.0638541728258133,
            1.0, -0.0894841775298119, -1.2914855480194092,
        };

        // Bradford CAT。colorjs.io がハードコードしている D65↔D50 の既製行列と同値
        static readonly double[] D65_TO_D50 =
        {
            1.0479297925449969, 0.022946870601609652, -0.05019226628920524,
            0.02962780877005599, 0.9904344267538799, -0.017073799063418826,
            -0.009243040646204504, 0.015055191490298152, 0.7518742814281371,
        };

        static readonly double[] D50_TO_D65 =
        {
            0.955473421488075, -0.02309845494876471, 0.06325924320057072,
            -0.0283697093338637, 1.0099953980813041, 0.021041441191917323,
            0.012314014864481998, -0.020507649298898964, 1.330365926242124,
        };

        static readonly double[] WHITE_D50 =
        {
            0.3457 / 0.3585, 1.0, (1.0 - 0.3457 - 0.3585) / 0.3585,
        };

        const double LAB_E = 216.0 / 24389.0;
        const double LAB_E3 = 24.0 / 116.0;
        const double LAB_K = 24389.0 / 27.0;

        // colorjs.io の oklch.fromBase が「無彩色」と見なす a/b の閾値
        const double ACHROMATIC_EPSILON = 0.0002;

        const double DEGREES_PER_RADIAN = 180.0 / Math.PI;
        const double RADIANS_PER_DEGREE = Math.PI / 180.0;

        // colorjs.io の inGamut 既定イプシロン。serialize が「はみ出しているか」を判定する幅
        const double GAMUT_EPSILON = 75e-6;

        // CSS Color 4 Gamut Mapping Algorithm のパラメータ
        const double GAMUT_JND = 0.02;
        const double GAMUT_PRECISION = 0.0001;

        #endregion

        #region Conversion

        /// <summary>Display P3（[0, 1] のガンマ付き）→ OKLCH。</summary>
        public static Oklch P3ToOklch(double r, double g, double b)
        {
            return OklabToOklch(XyzToOklab(Transform(P3_TO_XYZ, ToLinear(r), ToLinear(g), ToLinear(b))));
        }

        /// <summary>sRGB（[0, 1] のガンマ付き）→ OKLCH。</summary>
        public static Oklch SrgbToOklch(double r, double g, double b)
        {
            return OklabToOklch(SrgbToOklab(r, g, b));
        }

        /// <summary>sRGB → OKLab。</summary>
        public static Oklab SrgbToOklab(double r, double g, double b)
        {
            return XyzToOklab(Transform(SRGB_TO_XYZ, ToLinear(r), ToLinear(g), ToLinear(b)));
        }

        /// <summary>
        /// OKLCH → sRGB。範囲外（負・1 超）もそのまま返す。
        /// </summary>
        /// <remarks>
        /// クランプしないのは、原典が APCA 判定や deltaEOK をガモット外の値のまま行っているため。
        /// 表示用の値が必要なときは <see cref="OklchToBytes"/> を使う。
        /// </remarks>
        public static void OklchToSrgb(Oklch color, out double r, out double g, out double b)
        {
            Oklab lab = OklchToOklab(color);
            double[] xyz = OklabToXyz(lab);
            double[] rgb = Transform(XYZ_TO_SRGB, xyz[0], xyz[1], xyz[2]);
            r = FromLinear(rgb[0]);
            g = FromLinear(rgb[1]);
            b = FromLinear(rgb[2]);
        }

        /// <summary>
        /// OKLCH → OKLab。NaN 成分は 0 に置き換える。
        /// </summary>
        /// <remarks>
        /// colorjs.io の ColorSpace.to() が変換前に NaN 座標を 0 で埋めるため、無彩色（H = NaN）は
        /// 「a = b = 0」ではなく「色相 0 度」として扱われる。C がわずかでも残っていると赤側へ寄る
        /// ので、この差は最終バイト値に出る。
        /// </remarks>
        public static Oklab OklchToOklab(Oklch color)
        {
            double l = double.IsNaN(color.L) ? 0.0 : color.L;
            double c = double.IsNaN(color.C) ? 0.0 : color.C;
            double h = double.IsNaN(color.H) ? 0.0 : color.H;
            return new Oklab(l, c * Math.Cos(h * RADIANS_PER_DEGREE), c * Math.Sin(h * RADIANS_PER_DEGREE));
        }

        /// <summary>OKLab → OKLCH。a/b がともに微小なら色相を NaN にする。</summary>
        public static Oklch OklabToOklch(Oklab color)
        {
            double hue;
            if (Math.Abs(color.A) < ACHROMATIC_EPSILON && Math.Abs(color.B) < ACHROMATIC_EPSILON)
            {
                hue = double.NaN;
            }
            else
            {
                hue = Math.Atan2(color.B, color.A) * DEGREES_PER_RADIAN;
                hue = (hue % 360.0 + 360.0) % 360.0;
            }

            return new Oklch(color.L, Math.Sqrt(color.A * color.A + color.B * color.B), hue);
        }

        /// <summary>
        /// OKLab → CIE Lab(D50)。colorjs.io の既定補間空間なので、スケール混合はここで行う。
        /// </summary>
        public static Oklab OklabToLabD50(Oklab color)
        {
            double[] xyz65 = OklabToXyz(color);
            double[] xyz = Transform(D65_TO_D50, xyz65[0], xyz65[1], xyz65[2]);

            double f0 = LabF(xyz[0] / WHITE_D50[0]);
            double f1 = LabF(xyz[1] / WHITE_D50[1]);
            double f2 = LabF(xyz[2] / WHITE_D50[2]);

            return new Oklab(116.0 * f1 - 16.0, 500.0 * (f0 - f1), 200.0 * (f1 - f2));
        }

        /// <summary>CIE Lab(D50) → OKLab。</summary>
        public static Oklab LabD50ToOklab(Oklab cielab)
        {
            double f1 = (cielab.L + 16.0) / 116.0;
            double f0 = cielab.A / 500.0 + f1;
            double f2 = f1 - cielab.B / 200.0;

            double x = (f0 > LAB_E3 ? f0 * f0 * f0 : (116.0 * f0 - 16.0) / LAB_K) * WHITE_D50[0];
            double y = (cielab.L > 8.0 ? Cube((cielab.L + 16.0) / 116.0) : cielab.L / LAB_K) * WHITE_D50[1];
            double z = (f2 > LAB_E3 ? f2 * f2 * f2 : (116.0 * f2 - 16.0) / LAB_K) * WHITE_D50[2];

            double[] xyz65 = Transform(D50_TO_D65, x, y, z);
            return XyzToOklab(xyz65);
        }

        #endregion

        #region Gamut

        /// <summary>
        /// OKLCH → 0〜255 の sRGB バイト。ガモット外は CSS Color 4 の Gamut Mapping で写す。
        /// </summary>
        /// <remarks>
        /// 原典の `color.to('srgb').toString({format:'hex'})` と同じ経路。colorjs.io の serialize は
        /// 既定でガモットマップを掛けるため、単純なクリップに置き換えると彩度の高いアクセントで
        /// 色相が転ぶ。
        /// </remarks>
        public static Rgba32 OklchToBytes(Oklch color)
        {
            OklchToSrgbGamutMapped(color, out double r, out double g, out double b);
            return new Rgba32(ToByte(r), ToByte(g), ToByte(b), 255);
        }

        /// <summary>OKLCH → sRGB。ガモット外なら CSS Color 4 Gamut Mapping Algorithm を適用する。</summary>
        public static void OklchToSrgbGamutMapped(Oklch color, out double r, out double g, out double b)
        {
            OklchToSrgb(color, out r, out g, out b);
            if (InGamut(r, g, b, GAMUT_EPSILON))
            {
                return;
            }

            if (color.L >= 1.0)
            {
                r = 1.0;
                g = 1.0;
                b = 1.0;
                return;
            }

            if (color.L <= 0.0)
            {
                r = 0.0;
                g = 0.0;
                b = 0.0;
                return;
            }

            if (InGamut(r, g, b, 0.0))
            {
                return;
            }

            double min = 0.0;
            double max = color.C;
            bool minInGamut = true;
            Oklch current = color;

            double clippedR = Clamp01(r);
            double clippedG = Clamp01(g);
            double clippedB = Clamp01(b);
            double error = DeltaEOKLab(SrgbToOklab(clippedR, clippedG, clippedB), OklchToOklab(current));

            if (error < GAMUT_JND)
            {
                r = clippedR;
                g = clippedG;
                b = clippedB;
                return;
            }

            while (max - min > GAMUT_PRECISION)
            {
                double chroma = (min + max) * 0.5;
                current.C = chroma;
                OklchToSrgb(current, out double cr, out double cg, out double cb);

                if (minInGamut && InGamut(cr, cg, cb, 0.0))
                {
                    min = chroma;
                    continue;
                }

                clippedR = Clamp01(cr);
                clippedG = Clamp01(cg);
                clippedB = Clamp01(cb);
                error = DeltaEOKLab(SrgbToOklab(clippedR, clippedG, clippedB), OklchToOklab(current));

                if (error < GAMUT_JND)
                {
                    if (GAMUT_JND - error < GAMUT_PRECISION)
                    {
                        break;
                    }

                    minInGamut = false;
                    min = chroma;
                }
                else
                {
                    max = chroma;
                }
            }

            r = clippedR;
            g = clippedG;
            b = clippedB;
        }

        static bool InGamut(double r, double g, double b, double epsilon)
        {
            return InRange(r, epsilon) && InRange(g, epsilon) && InRange(b, epsilon);
        }

        static bool InRange(double value, double epsilon)
        {
            if (double.IsNaN(value))
            {
                return true;
            }

            return value >= -epsilon && value <= 1.0 + epsilon;
        }

        #endregion

        #region Metrics

        /// <summary>OKLab 上のユークリッド距離（deltaEOK）。</summary>
        public static double DeltaEOKLab(Oklab left, Oklab right)
        {
            double dl = left.L - right.L;
            double da = left.A - right.A;
            double db = left.B - right.B;
            return Math.Sqrt(dl * dl + da * da + db * db);
        }

        /// <summary>OKLCH 同士の deltaEOK。無彩色は色相 0 度として扱う（原典と同じ）。</summary>
        public static double DeltaEOK(Oklch left, Oklch right)
        {
            return DeltaEOKLab(OklchToOklab(left), OklchToOklab(right));
        }

        /// <summary>
        /// APCA-W3 (0.0.98G) のコントラスト。引数順は colorjs.io の contrastAPCA に合わせて
        /// 「背景・前景」。返り値は Lc（符号付き）。
        /// </summary>
        /// <remarks>
        /// ガモット外の入力をクランプしないのも原典どおり。負の輝度は累乗で NaN になり、
        /// radix.ts の getTextColor では「白では読めない」判定に落ちる。
        /// </remarks>
        public static double ContrastApca(
            double backgroundR, double backgroundG, double backgroundB,
            double foregroundR, double foregroundG, double foregroundB)
        {
            double yText = ApcaSoftClamp(ApcaLuminance(foregroundR, foregroundG, foregroundB));
            double yBackground = ApcaSoftClamp(ApcaLuminance(backgroundR, backgroundG, backgroundB));

            double contrast = 0.0;
            if (Math.Abs(yBackground - yText) >= 0.0005)
            {
                contrast = yBackground > yText
                    ? (Math.Pow(yBackground, 0.56) - Math.Pow(yText, 0.57)) * 1.14
                    : (Math.Pow(yBackground, 0.65) - Math.Pow(yText, 0.62)) * 1.14;
            }

            double scaled;
            if (Math.Abs(contrast) < 0.1)
            {
                scaled = 0.0;
            }
            else if (contrast > 0.0)
            {
                scaled = contrast - 0.027;
            }
            else
            {
                scaled = contrast + 0.027;
            }

            return scaled * 100.0;
        }

        static double ApcaLuminance(double r, double g, double b)
        {
            return ApcaLinearize(r) * 0.2126729
                + ApcaLinearize(g) * 0.7151522
                + ApcaLinearize(b) * 0.0721750;
        }

        static double ApcaLinearize(double value)
        {
            double sign = value < 0.0 ? -1.0 : 1.0;
            return sign * Math.Pow(Math.Abs(value), 2.4);
        }

        static double ApcaSoftClamp(double luminance)
        {
            if (luminance >= 0.022)
            {
                return luminance;
            }

            return luminance + Math.Pow(0.022 - luminance, 1.414);
        }

        #endregion

        #region Channels

        /// <summary>[0, 1] のチャンネルを 0〜255 へ。JS の Math.round と同じ「上寄せ」丸め。</summary>
        public static int ToByte(double channel)
        {
            if (double.IsNaN(channel))
            {
                return 0;
            }

            int value = (int)Math.Floor(channel * 255.0 + 0.5);
            return value < 0 ? 0 : value > 255 ? 255 : value;
        }

        static double Clamp01(double value)
        {
            return value < 0.0 ? 0.0 : value > 1.0 ? 1.0 : value;
        }

        // sRGB / Display P3 共通の伝達関数。負値は符号を保って折り返す（colorjs.io と同じ）
        static double ToLinear(double value)
        {
            double sign = value < 0.0 ? -1.0 : 1.0;
            double absolute = value * sign;
            if (absolute <= 0.04045)
            {
                return value / 12.92;
            }

            return sign * Math.Pow((absolute + 0.055) / 1.055, 2.4);
        }

        static double FromLinear(double value)
        {
            double sign = value < 0.0 ? -1.0 : 1.0;
            double absolute = value * sign;
            if (absolute > 0.0031308)
            {
                return sign * (1.055 * Math.Pow(absolute, 1.0 / 2.4) - 0.055);
            }

            return 12.92 * value;
        }

        static Oklab XyzToOklab(double[] xyz)
        {
            double[] lms = Transform(XYZ_TO_LMS, xyz[0], xyz[1], xyz[2]);
            double[] lab = Transform(LMS_TO_LAB, Math.Cbrt(lms[0]), Math.Cbrt(lms[1]), Math.Cbrt(lms[2]));
            return new Oklab(lab[0], lab[1], lab[2]);
        }

        static double[] OklabToXyz(Oklab color)
        {
            double[] lms = Transform(LAB_TO_LMS, color.L, color.A, color.B);
            return Transform(LMS_TO_XYZ, Cube(lms[0]), Cube(lms[1]), Cube(lms[2]));
        }

        static double LabF(double value)
        {
            return value > LAB_E ? Math.Cbrt(value) : (LAB_K * value + 16.0) / 116.0;
        }

        static double Cube(double value)
        {
            return value * value * value;
        }

        // 行優先 3x3 × ベクトル。要素順を JS の multiplyMatrices と一致させて誤差の出方を揃える
        static double[] Transform(double[] matrix, double x, double y, double z)
        {
            return new[]
            {
                matrix[0] * x + matrix[1] * y + matrix[2] * z,
                matrix[3] * x + matrix[4] * y + matrix[5] * z,
                matrix[6] * x + matrix[7] * y + matrix[8] * z,
            };
        }

        #endregion
    }

    /// <summary>
    /// CSS の cubic-bezier と同じ三次ベジェイージング。gre/bezier-easing 互換。
    /// </summary>
    /// <remarks>
    /// サンプル表を float で持つのは原典が Float32Array を使っているため。double にすると
    /// ニュートン法の初期値がわずかに変わり、Radix スケールの明度が最下位ビットで揺れる。
    /// y は [0, 1] 外でもよい（Radix の lightModeEasing は y = 2 を使う）。
    /// </remarks>
    public sealed class CubicBezierEasing
    {
        #region Constants

        const int SPLINE_TABLE_SIZE = 11;
        const double SAMPLE_STEP = 1.0 / (SPLINE_TABLE_SIZE - 1.0);
        const double NEWTON_MIN_SLOPE = 0.001;
        const int NEWTON_ITERATIONS = 4;
        const double SUBDIVISION_PRECISION = 0.0000001;
        const int SUBDIVISION_MAX_ITERATIONS = 10;

        #endregion

        readonly double _x1;
        readonly double _y1;
        readonly double _x2;
        readonly double _y2;
        readonly bool _isLinear;
        readonly float[] _samples;

        public CubicBezierEasing(double x1, double y1, double x2, double y2)
        {
            if (x1 < 0.0 || x1 > 1.0 || x2 < 0.0 || x2 > 1.0)
            {
                throw new ArgumentOutOfRangeException(nameof(x1), "bezier x values must be in [0, 1] range");
            }

            _x1 = x1;
            _y1 = y1;
            _x2 = x2;
            _y2 = y2;
            _isLinear = x1 == y1 && x2 == y2;

            if (_isLinear)
            {
                _samples = null;
                return;
            }

            _samples = new float[SPLINE_TABLE_SIZE];
            for (int i = 0; i < SPLINE_TABLE_SIZE; i++)
            {
                _samples[i] = (float)Calc(i * SAMPLE_STEP, x1, x2);
            }
        }

        /// <summary>x（[0, 1]）に対する y。</summary>
        public double Evaluate(double x)
        {
            if (_isLinear)
            {
                return x;
            }

            if (x == 0.0)
            {
                return 0.0;
            }

            if (x == 1.0)
            {
                return 1.0;
            }

            return Calc(TForX(x), _y1, _y2);
        }

        double TForX(double x)
        {
            double intervalStart = 0.0;
            int currentSample = 1;
            const int LAST_SAMPLE = SPLINE_TABLE_SIZE - 1;

            for (; currentSample != LAST_SAMPLE && _samples[currentSample] <= x; ++currentSample)
            {
                intervalStart += SAMPLE_STEP;
            }

            --currentSample;

            double distance = (x - _samples[currentSample])
                / (_samples[currentSample + 1] - _samples[currentSample]);
            double guess = intervalStart + distance * SAMPLE_STEP;

            double initialSlope = Slope(guess, _x1, _x2);
            if (initialSlope >= NEWTON_MIN_SLOPE)
            {
                return NewtonRaphson(x, guess);
            }

            if (initialSlope == 0.0)
            {
                return guess;
            }

            return BinarySubdivide(x, intervalStart, intervalStart + SAMPLE_STEP);
        }

        double NewtonRaphson(double x, double guess)
        {
            double t = guess;
            for (int i = 0; i < NEWTON_ITERATIONS; i++)
            {
                double slope = Slope(t, _x1, _x2);
                if (slope == 0.0)
                {
                    return t;
                }

                t -= (Calc(t, _x1, _x2) - x) / slope;
            }

            return t;
        }

        double BinarySubdivide(double x, double lower, double upper)
        {
            double currentT;
            double currentX;
            int i = 0;
            do
            {
                currentT = lower + (upper - lower) * 0.5;
                currentX = Calc(currentT, _x1, _x2) - x;
                if (currentX > 0.0)
                {
                    upper = currentT;
                }
                else
                {
                    lower = currentT;
                }
            }
            while (Math.Abs(currentX) > SUBDIVISION_PRECISION && ++i < SUBDIVISION_MAX_ITERATIONS);

            return currentT;
        }

        static double Calc(double t, double a1, double a2)
        {
            return ((CoefficientA(a1, a2) * t + CoefficientB(a1, a2)) * t + CoefficientC(a1)) * t;
        }

        static double Slope(double t, double a1, double a2)
        {
            return 3.0 * CoefficientA(a1, a2) * t * t + 2.0 * CoefficientB(a1, a2) * t + CoefficientC(a1);
        }

        static double CoefficientA(double a1, double a2)
        {
            return 1.0 - 3.0 * a2 + 3.0 * a1;
        }

        static double CoefficientB(double a1, double a2)
        {
            return 3.0 * a2 - 6.0 * a1;
        }

        static double CoefficientC(double a1)
        {
            return 3.0 * a1;
        }
    }
}
