using System;
using System.Linq;
using System.Reflection;
var asm = Assembly.LoadFrom(@"E:\SteamLibrary\steamapps\common\Stardew Valley\Stardew Valley.dll");
var type = asm.GetType("StardewValley.GameLocation");
foreach (var p in type.GetProperties(BindingFlags.Public|BindingFlags.Instance).Where(p => p.Name.ToLower().Contains("furniture")).OrderBy(p => p.Name))
    Console.WriteLine("P " + p.PropertyType + " " + p.Name);
foreach (var f in type.GetFields(BindingFlags.Public|BindingFlags.Instance).Where(f => f.Name.ToLower().Contains("furniture")).OrderBy(f => f.Name))
    Console.WriteLine("F " + f.FieldType + " " + f.Name);
