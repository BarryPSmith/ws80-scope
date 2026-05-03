using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using uint8_t = byte;
using uint32_t = uint;
using uint16_t = ushort;
using int8_t = sbyte;
using int16_t = short;
using int32_t = int;
using q2_13 = short;
using q1_14 = short;
using q18_13 = int;
using q17_14 = int;

namespace Ultrasonics
{
    class FirmwareCopy
    {
        const int WIND_SAMPLE_SIZE = 256;
        public uint8_t[] g_windRingCounts = new uint8_t[6];
        public uint32_t[,] g_signalPowers = new uint32_t[6,2];
        public uint16_t[] g_wind_measurement = new uint16_t[WIND_SAMPLE_SIZE];

        static int16_t[] sin24_5 = { 0, 519, 1005, 1425, 1751, 1963, 2047, 1997, 1816, 1516, 1117, 645, 131, -391, -889, -1328, -1680, -1922, -2039, -2022, -1873, -1601, -1225, -769, -262, 262, 769, 1225, 1601, 1873, 2022, 2039, 1922, 1680, 1328, 889, 391, -131, -645, -1117, -1516, -1816, -1997, -2047, -1963, -1751, -1425, -1005, -519 };
        static int16_t[] cos24_5 = { 2048, 1981, 1784, 1471, 1062, 583, 66, -456, -947, -1377, -1716, -1944, -2044, -2010, -1845, -1559, -1172, -707, -197, 327, 829, 1277, 1641, 1898, 2031, 2031, 1898, 1641, 1277, 829, 327, -197, -707, -1172, -1559, -1845, -2010, -2044, -1944, -1716, -1377, -947, -456, 66, 583, 1062, 1471, 1784, 1981 };
        // Window is a hann window, with maximum value range 0 -> 4096 == 0 -> 2^12
        // double Hann(int i) => 2048 * Math.Sin(Math.PI * (i + 2) / (DataSize + 3)) * Math.Sin(Math.PI * (i + 2) / (DataSize + 3));
        static int16_t[] window = { 1, 3, 6, 11, 17, 24, 33, 43, 54, 66, 80, 95, 112, 130, 148, 169, 190, 213, 236, 261, 288, 315, 343, 373, 404, 435, 468, 502, 537, 572, 609, 647, 685, 725, 765, 806, 848, 891, 935, 979, 1024, 1070, 1116, 1163, 1210, 1258, 1307, 1356, 1405, 1455, 1505, 1556, 1607, 1658, 1710, 1761, 1813, 1865, 1917, 1970, 2022, 2074, 2126, 2179, 2231, 2283, 2335, 2386, 2438, 2489, 2540, 2591, 2641, 2691, 2740, 2789, 2838, 2886, 2933, 2980, 3026, 3072, 3117, 3161, 3205, 3248, 3290, 3331, 3371, 3411, 3449, 3487, 3524, 3559, 3594, 3628, 3661, 3692, 3723, 3753, 3781, 3808, 3835, 3860, 3883, 3906, 3927, 3948, 3966, 3984, 4001, 4016, 4030, 4042, 4053, 4063, 4072, 4079, 4085, 4090, 4093, 4095, 4096, 4095, 4093, 4090, 4085, 4079, 4072, 4063, 4053, 4042, 4030, 4016, 4001, 3984, 3966, 3948, 3927, 3906, 3883, 3860, 3835, 3808, 3781, 3753, 3723, 3692, 3661, 3628, 3594, 3559, 3524, 3487, 3449, 3411, 3371, 3331, 3290, 3248, 3205, 3161, 3117, 3072, 3026, 2980, 2933, 2886, 2838, 2789, 2740, 2691, 2641, 2591, 2540, 2489, 2438, 2386, 2335, 2283, 2231, 2179, 2126, 2074, 2022, 1970, 1917, 1865, 1813, 1761, 1710, 1658, 1607, 1556, 1505, 1455, 1405, 1356, 1307, 1258, 1210, 1163, 1116, 1070, 1024, 979, 935, 891, 848, 806, 765, 725, 685, 647, 609, 572, 537, 502, 468, 435, 404, 373, 343, 315, 288, 261, 236, 213, 190, 169, 148, 130, 112, 95, 80, 66, 54, 43, 33, 24, 17, 11, 6, 3, 1 };

        q1_14[,] s_radiusVectors = {
              { qn_14_sqrt1_2 , -qn_14_sqrt1_2 } , // 0, 1
              { -qn_14_sqrt1_2, -qn_14_sqrt1_2 } , // 0, 2
              { 0,        -qn_14_one     } , // 0, 3
              { -qn_14_one,     0        } , // 1, 2
              { -qn_14_sqrt1_2, -qn_14_sqrt1_2 } , // 1, 3
              { qn_14_sqrt1_2 , -qn_14_sqrt1_2 } };// 2, 3
        q1_14[,] s_tangentVectors = {
              { qn_14_sqrt1_2,  qn_14_sqrt1_2 } ,
              { qn_14_sqrt1_2, -qn_14_sqrt1_2 } ,
              { qn_14_one    ,  0       } ,
              { 0      ,  qn_14_one     } ,
              { qn_14_sqrt1_2, -qn_14_sqrt1_2 } ,
              { qn_14_sqrt1_2,  qn_14_sqrt1_2 } };

        q2_13[,] s_signalPhases = new q2_13[6,2];


        const q1_14 qn_14_sqrt1_2 = 11585; // sqrt(1/2)
        const q1_14 qn_14_one = 16384;
        const q2_13 qn_13_pi = 25736;

        void WIND_PRINT(params object[] objs)
        { }


        #region windCalc.c
        public void processWindWaveform(uint8_t channel, uint8_t direction)
        {
            // Our source data is 12 bit ADC values. If we don't normalise them,
            // they have a maximum value of 2^12 each
            // It probably makes the most sense to have window and the trig functions 
            // have the same resolution as our data
            // Our data will range from -2^12 to 2^12
            // So cos will range from -2^11 to +2^11
            // window will range from 0 to 2^12
            // Multiply all three, and we get 35 bits.
            // So we need to lose 4 bits before the final multiplication.
            // Then, sum 2^8 values
            // So we need to lose 12 bits in total.

            const uint8_t sampleMax = 245; // 49 * 5;
            const uint8_t cycleLength = 49;
            int32_t sum = 0;
            for (uint8_t i = 0; i < sampleMax; i++)
                sum += g_wind_measurement[i];
            int32_t avg = sum / sampleMax;
            int32_t cosSum = 0;
            int32_t sinSum = 0;
            for (uint8_t i = 0; i < sampleMax; i++)
            {
                int16_t rawVal = (short) g_wind_measurement[i];
                int32_t val = (int)((rawVal - avg) * window[i] >> 4); // rawVal max value is 2^12, window maxvalue is 2^12, Maximum intermediate value is 2^24, val maxvalue is 2^20
                int32_t cosVal = val * cos24_5[i % cycleLength] >> 8; //2^20, 2^11, maximum intermediate 2^31, cosVal maxvalue is 2^23
                cosSum += cosVal; // 2^23, 2^8 times, maximum value is 2^31
                int32_t sinVal = val * sin24_5[i % cycleLength] >> 8; // as above
                sinSum += sinVal; // as above
            }
            const int32_t window_sum = 507782; // 2^19
            int32_t cos16 = (int)(cosSum / window_sum); // maximum of 2^12. More likely 2^8 or less.
            int32_t sin16 = (int)(sinSum / window_sum);
            uint32_t power = (uint)(cos16 * cos16 + sin16 * sin16); // Maximum of 2^24
            g_signalPowers[channel,direction] = power;

            double phase = Math.Atan2(sinSum, cosSum);
            s_signalPhases[channel,direction] = (short)(phase * (1 << 13)); // phase maximum +/- pi, this has maximum value +/- 2^15
            WIND_PRINT("Wind: Process waveform complete!\r\n");
        }

        q18_13 normalise_angle(q18_13 angle)
        {
            while (angle < -qn_13_pi)
                angle += 2 * qn_13_pi;
            while (angle > qn_13_pi)
                angle -= qn_13_pi;
            return angle;
        }


        public void calculate_wind(out int16_t x_cmps, out int16_t y_cmps)
        {
            // time in nanoseconds
            q18_13[] signalPhaseDiffs = new int32_t[6];
            for (uint8_t channel = 0; channel < 6; channel++)
            {
                q18_13 phaseDiff = normalise_angle(
                    (q18_13)s_signalPhases[channel,0] - s_signalPhases[channel,1]);
                signalPhaseDiffs[channel] = phaseDiff; // maximum 2^15
            }
            uint32_t best_residual = 0xFFFFFFFF;
            int16_t best_x = 0;
            int16_t best_y = 0;
            for (int8_t ch2Wrap = -1; ch2Wrap <= 1; ch2Wrap++)
            {
                for (int8_t ch3Wrap = -1; ch3Wrap <= 1; ch3Wrap++)
                {
                    int32_t sum_x = 0,
                            sum_y = 0;
                    for (uint8_t channel = 0; channel < 6; channel++)
                    {
                        q18_13 phaseDiff = signalPhaseDiffs[channel];
                        double phaseDiffFloat = phaseDiff / Math.Pow(2, 13);
                        if (channel == 2)
                            phaseDiff += 2 * qn_13_pi * ch2Wrap;
                        if (channel == 3)
                            phaseDiff += 2 * qn_13_pi * ch3Wrap;
                        phaseDiffFloat = phaseDiff / Math.Pow(2, 13);
                        int16_t x, y;
                        get_radius_vector_cmps(out x, out y, phaseDiff, channel);
                        sum_x += x; // x=2^14, summed 6 times => sum_x maximum value 2^17
                        sum_y += y;
                    }
                    int16_t bestFit_x = (short)(sum_x / 3); // bestfit maximum value 2^15 (reality is 2^14, but headroom is nice)
                    int16_t bestFit_y = (short)(sum_y / 3);
                    uint32_t residual = 0;
                    for (uint8_t channel = 0; channel < 6; channel++)
                    {
                        q18_13 phaseDiff = signalPhaseDiffs[channel];
                        if (channel == 2)
                            phaseDiff += 2 * qn_13_pi * ch2Wrap;
                        if (channel == 3)
                            phaseDiff += 2 * qn_13_pi * ch3Wrap;
                        int16_t x, y;
                        get_radius_vector_cmps(out x, out y, phaseDiff, channel);
                        int16_t diff_x = (short)(bestFit_x - x); // diff_x maximum 2^15
                        int16_t diff_y = (short)(bestFit_y - y);
                        q1_14 tangent_x = s_tangentVectors[channel,0];
                        q1_14 tangent_y = s_tangentVectors[channel,1];
                        int16_t dot_x = (short)((int32_t)diff_x * tangent_x >> 14); // dot_x maximum 2^15
                        int16_t dot_y = (short)((int32_t)diff_y * tangent_y >> 14);
                        uint32_t dist_2 = (uint)((int32_t)diff_x * diff_x + (int32_t)diff_y * diff_y -
                            ((int32_t)dot_x * dot_x + (int32_t)dot_y * dot_y)); // maximum intermediate value 2^31.
                        residual += dist_2 >> 2; // 2^31 summed 6 times but removed 2 bits => maximum 2^31.
                    }
                    if (residual < best_residual)
                    {
                        best_residual = residual;
                        best_x = bestFit_x;
                        best_y = bestFit_y;
                    }
                }
            }
            x_cmps = best_x;
            y_cmps = best_y;
            WIND_PRINT("Wind: calculate_wind complete!\r\n");
        }

        // Maximum returned values 2^14
        void get_radius_vector_cmps(out int16_t x_cmps, out int16_t y_cmps, q18_13 phaseDiff, uint8_t channel)
        {
            uint32_t phaseToTime_ns = 3900; //(uint32_t)(24.5 * 1000 / (2 * 3.141259));
                                            // Phase maximum value is +/- 3pi => 10.
                                            // delay_ns maximum value => +/- 40,000 (40us)
            q18_13 timeDiff_ns = (int)(phaseDiff * phaseToTime_ns);
            int32_t delay_ns = timeDiff_ns >> 13;
            int16_t speed_cmps = get_speed_cmps(channel, delay_ns);
            x_cmps = (short)(speed_cmps * (q17_14)s_radiusVectors[channel,0] >> 14);
            y_cmps = (short)(speed_cmps * (q17_14)s_radiusVectors[channel,1] >> 14);
        }

        uint16_t g_temperature_degC_x10 = 250;
        // Maximum windspeed 100m/s
        // = 10,000 cm/s
        // 14 bits
        int16_t get_speed_cmps(uint8_t channel, int32_t timeDiff_ns)
        {
            uint32_t speedOfSound_dmps = (uint)(3315 + (g_temperature_degC_x10 * 61) / 100);
            uint32_t effective_length_um;
            if (channel == 2 || channel == 3)
                effective_length_um = 37000;
            else
                effective_length_um = 16000;
            // units: (dm * dm / um) / (s * s / ns) = (10^4m) / Gs? = 1E5 * m/s
            // So divide by 1000 to get cm/s
            // Formula is V = 1/2 * v_sound^2 * delta_T / length
            // Maximum time diff is 40,000
            // Maximum returned value is 16200 == 2^14
            return (short)((speedOfSound_dmps * speedOfSound_dmps / effective_length_um) * timeDiff_ns / (2 * 1000));
        }
        #endregion windCalc.c

        #region windPhys.c
        public void updatePhysicalParamters()
        {
            const uint32_t min_power = 25000;
            const uint32_t barrier_power = 45000;
            const uint32_t max_power = 90000;
            const uint8_t minRings = 2;
            const uint8_t maxRings = 12;
            for (int channel = 0; channel < 6; channel++)
            {
                if (
                        (g_signalPowers[channel,0] > max_power ||
                            g_signalPowers[channel,1] > max_power) &&
                        g_windRingCounts[channel] > minRings)
                    g_windRingCounts[channel]--;
                else if (
                        (g_signalPowers[channel,0] < min_power ||
                            g_signalPowers[channel,1] < min_power) &&
                        (g_signalPowers[channel,0] < barrier_power &&
                            g_signalPowers[channel,1] < barrier_power) &&
                        g_windRingCounts[channel] < maxRings)
                    g_windRingCounts[channel]++;
            }
        }

        #endregion windPhys.c
    }
}
