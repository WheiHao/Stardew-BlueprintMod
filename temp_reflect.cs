using System;
using System.Linq;
using System.Reflection;
using StardewValley;
using StardewValley.Objects;

var asm = Assembly.LoadFrom(@"E:\SteamLibrary\steamapps\common\Stardew Valley\Stardew Valley.dll");
var type = asm.GetType("StardewValley.Objects.Furniture");
Console.WriteLine(type);
foreach (var p in type.GetProperties(BindingFlags.Public|BindingFlags.Instance).OrderBy(p => p.Name))
    Console.WriteLine("P " + p.PropertyType.Name + " " + p.Name);
foreach (var f in type.GetFields(BindingFlags.Public|BindingFlags.Instance).OrderBy(f => f.Name).Take(40))
    Console.WriteLine("F " + f.FieldType.Name + " " + f.Name);
foreach (var m in type.GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly).OrderBy(m => m.Name))
    Console.WriteLine("M " + m.ReturnType.Name + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(x => x.ParameterType.Name+" "+x.Name)) + ")");
