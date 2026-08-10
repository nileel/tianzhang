using System;
using System.Reflection;
using NUnit.Framework;

namespace TianZhang.Tests
{
    public sealed class CharacterStateTests
    {
        [Test]
        public void CharacterResourcesCaptureAndRestoreWithoutCombatState()
        {
            Type type = Load("TianZhang.Character", "TianZhang.Character.CharacterResources");
            object resources = Activator.CreateInstance(type, 100, 25, 40, 10);
            object snapshot = Invoke(resources, "Capture");
            Invoke(resources, "Restore", snapshot);
            Assert.That(Get(resources, "CurrentHealth"), Is.EqualTo(25));
            Assert.That(Get(resources, "CurrentSpirit"), Is.EqualTo(10));
            Assert.That(type.GetProperty("Position"), Is.Null);
            Assert.That(type.GetProperty("CTBUnit"), Is.Null);
        }

        private static Type Load(string assemblyName, string typeName) { return Assembly.Load(assemblyName).GetType(typeName, true); }
        private static object Invoke(object target, string name, params object[] args) { return target.GetType().GetMethod(name).Invoke(target, args); }
        private static object Get(object target, string name) { return target.GetType().GetProperty(name).GetValue(target, null); }
    }
}
