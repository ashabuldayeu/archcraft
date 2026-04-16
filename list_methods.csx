var asm = System.Reflection.Assembly.LoadFrom("/c/Users/User/.nuget/packages/testcontainers/4.11.0/lib/net10.0/Testcontainers.dll");
foreach (var t in asm.GetExportedTypes())
    foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static))
        if (m.Name.Contains("Port") || m.Name.Contains("Until"))
            Console.WriteLine($"{t.Name}.{m.Name}");
