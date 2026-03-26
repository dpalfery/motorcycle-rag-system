using System;
using System.Reflection;
class P {
  static void Main() {
    var asm = Assembly.LoadFrom(Environment.ExpandEnvironmentVariables(@"%USERPROFILE%\.nuget\packages\pulumi.azurenative\3.13.0\lib\net6.0\Pulumi.AzureNative.dll"));
    var t = asm.GetType("Pulumi.AzureNative.CognitiveServices.AccountArgs");
    var p = t.GetProperty("Identity");
    Console.WriteLine(p.PropertyType.FullName);
    var it = p.PropertyType;
    foreach (var prop in it.GetProperties()) Console.WriteLine($"{prop.Name}:{prop.PropertyType.FullName}");
    var enumType = asm.GetType("Pulumi.AzureNative.CognitiveServices.ManagedServiceIdentityType");
    Console.WriteLine(enumType?.FullName ?? "no enum");
    if (enumType != null) foreach (var name in Enum.GetNames(enumType)) Console.WriteLine(name);
  }
}
