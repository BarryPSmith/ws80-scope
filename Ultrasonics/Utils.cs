using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Ultrasonics
{
    public static class Utils
    {
        public static bool IsInDesignMode
            => DesignerProperties.GetIsInDesignMode(new DependencyObject());

        public static Complex SumC<T>(this IEnumerable<T> source, Func<T, Complex> selector)
        {
            Complex ret = 0;
            foreach (var v in source)
                ret += selector(v);
            return ret;
        }
    }
}
