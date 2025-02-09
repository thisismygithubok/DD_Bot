// /dd_bot/src/dd_bot.bot/SystemMetrics.cs
using System;
using System.IO;

public static class SystemMetrics
{
    public static double GetCpuUsage()
    {
        try
        {
            var cpuInfo = File.ReadAllLines("/host_proc/stat")[0];
            var cpuData = cpuInfo.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            var user = double.Parse(cpuData[1]);
            var nice = double.Parse(cpuData[2]);
            var system = double.Parse(cpuData[3]);
            var idle = double.Parse(cpuData[4]);

            var total = user + nice + system + idle;

            var cpuUsage = ((user + nice + system) / total) * 100.0;
            return Math.Round(cpuUsage, 2);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading CPU usage: {ex.Message}");
            return 0.0;
        }
    }

    public static double GetMemoryUsage()
    {
        try
        {
            var memInfo = File.ReadAllLines("/host_proc/meminfo");
            var totalMem = double.Parse(memInfo[0].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[1]);
            var freeMem = double.Parse(memInfo[1].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[1]);

            var usedMem = totalMem - freeMem;
            var memoryUsage = (usedMem / totalMem) * 100.0;
            return Math.Round(memoryUsage, 2);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading memory usage: {ex.Message}");
            return 0.0;
        }
    }
}
