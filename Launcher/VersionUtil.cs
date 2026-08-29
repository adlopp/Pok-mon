using System;

namespace UltraYeaLauncher
{
    internal static class VersionUtil
    {
        /// <summary>Quita la "v" inicial y espacios: "v1.2.3" -&gt; "1.2.3".</summary>
        public static string Normalize(string? v)
            => (v ?? "").Trim().TrimStart('v', 'V').Trim();

        /// <summary>&gt;0 si <paramref name="a"/> es más nueva que <paramref name="b"/>; &lt;0 si más vieja; 0 si igual.</summary>
        public static int Compare(string a, string b)
        {
            string[] pa = Split(Normalize(a));
            string[] pb = Split(Normalize(b));
            int n = Math.Max(pa.Length, pb.Length);

            for (int i = 0; i < n; i++)
            {
                string sa = i < pa.Length ? pa[i] : "0";
                string sb = i < pb.Length ? pb[i] : "0";

                bool na = long.TryParse(sa, out long ia);
                bool nb = long.TryParse(sb, out long ib);

                int c = (na && nb)
                    ? ia.CompareTo(ib)
                    : string.CompareOrdinal(sa, sb);

                if (c != 0) return Math.Sign(c);
            }
            return 0;
        }

        public static bool IsNewer(string candidate, string current)
            => Compare(candidate, current) > 0;

        private static string[] Split(string v)
            => v.Length == 0
                ? new[] { "0" }
                : v.Split(new[] { '.', '-', '+', '_' }, StringSplitOptions.RemoveEmptyEntries);
    }
}
