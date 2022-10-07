using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WindowsFormsApplication1
{
    public class SizeUnit
    {
        public static SizeUnit B { get; } = new SizeUnit("decimal", "byte", "B", 0);
        public static SizeUnit kB { get; } = new SizeUnit("decimal", "kilobyte", "kB", 1);
        public static SizeUnit KiB { get; } = new SizeUnit("binary", "kibibyte", "KiB", 1);
        public static SizeUnit MB { get; } = new SizeUnit("decimal", "megabyte", "MB", 2);
        public static SizeUnit MiB { get; } = new SizeUnit("binary", "mebibyte", "MiB", 2);
        public static SizeUnit GB { get; } = new SizeUnit("decimal", "gigabyte", "GB", 3);
        public static SizeUnit GiB { get; } = new SizeUnit("binary", "gibibyte", "GiB", 3);
        public static SizeUnit TB { get; } = new SizeUnit("decimal", "terabyte", "TB", 4);
        public static SizeUnit TiB { get; } = new SizeUnit("binary", "tebibyte", "TiB", 4);
        public static SizeUnit PB { get; } = new SizeUnit("decimal", "petabyte", "PB", 5);
        public static SizeUnit PiB { get; } = new SizeUnit("binary", "pebibyte", "PiB", 5);
        public static SizeUnit EB { get; } = new SizeUnit("decimal", "exabyte", "EB", 6);
        public static SizeUnit EiB { get; } = new SizeUnit("binary", "exbibyte", "EiB", 6);
        public static SizeUnit ZB { get; } = new SizeUnit("decimal", "zettabyte", "ZB", 7);
        public static SizeUnit ZiB { get; } = new SizeUnit("binary", "zebibyte", "ZiB", 7);
        public static SizeUnit YB { get; } = new SizeUnit("decimal", "yottabyte", "YB", 8);
        public static SizeUnit YiB { get; } = new SizeUnit("binary", "yobibyte", "YiB", 8);

        public string SizeUnitType { get; private set; }
        public string Name { get; private set; }
        public string ShortName { get; private set; }
        public int Power { get; private set; }

        private SizeUnit(string sizeunittype, string name, string shortname, int power)
        {
            SizeUnitType = sizeunittype;
            Name = name;
            ShortName = shortname;
            Power = power;
        }
    }

    public class ConvertFileSize
    {
        public double ConvertSize(Double value, SizeUnit oldUnit, SizeUnit newUnit)
        {

            // SELECT CASE intead
            int OldUnitConversionBytes = 1000;
            int NewUnitConversionBytes = 1000;
            switch (oldUnit.SizeUnitType)
            {
                case "decimal":
                    OldUnitConversionBytes = 1000;
                    break;
                case "binary":
                    OldUnitConversionBytes = 1024;
                    break;
            }
            switch (newUnit.SizeUnitType)
            {
                case "decimal":
                    NewUnitConversionBytes = 1000;
                    break;
                case "binary":
                    NewUnitConversionBytes = 1024;
                    break;
            }

            return ((value * Math.Pow(OldUnitConversionBytes, oldUnit.Power)) / Math.Pow(NewUnitConversionBytes, newUnit.Power));


            // OLD OLD OLD
            //if (oldUnit.SizeUnitType == "decimal" && newUnit.SizeUnitType == "decimal")
            //{
            //    // return (value * Math.Pow(1000, oldUnit.Power)); Converts old power to bytes
            //    // return (value / Math.Pow(1000, newUnit.Power)); Converts bytes value to new power
            //    return ((value * Math.Pow(1000, oldUnit.Power)) / Math.Pow(1000, newUnit.Power));
            //}

            //if (oldUnit.SizeUnitType == "binary" && newUnit.SizeUnitType == "binary")
            //{
            //    return ((value * Math.Pow(1024, oldUnit.Power)) / Math.Pow(1024, newUnit.Power));
            //}

            //if (oldUnit.SizeUnitType == "decimal" && newUnit.SizeUnitType == "binary")
            //{
            //    return ((value * Math.Pow(1000, oldUnit.Power)) / Math.Pow(1024, newUnit.Power));
            //}

            //if (oldUnit.SizeUnitType == "binary" && newUnit.SizeUnitType == "decimal")
            //{
            //    return ((value * Math.Pow(1024, oldUnit.Power)) / Math.Pow(1000, newUnit.Power));
            //}

            // If something weird happened return 0
            //return 0;
        }
    }
}
