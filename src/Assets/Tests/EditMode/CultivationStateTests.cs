using System;
using System.Reflection;
using NUnit.Framework;

namespace TianZhang.Tests
{
    public sealed class CultivationStateTests
    {
        [Test]
        public void FoundationStateRoundTripsIndependentlyOfCharacterImplementation()
        {
            Type type = Load("TianZhang.Cultivation", "TianZhang.Cultivation.FoundationState");
            object state = Activator.CreateInstance(type, 2, 100f, 1);
            Invoke(state, "Advance", 25f);
            object snapshot = Invoke(state, "Capture");
            object restored = Activator.CreateInstance(type, 0, 0f, 0);
            Invoke(restored, "Restore", snapshot);
            Assert.That(Get(restored, "Phase"), Is.EqualTo(2));
            Assert.That(Get(restored, "ContinuousProgress"), Is.EqualTo(125f));
            foreach (AssemblyName reference in type.Assembly.GetReferencedAssemblies())
                Assert.That(reference.Name, Is.Not.EqualTo("TianZhang.Domain"));
        }

        private static Type Load(string assemblyName, string typeName) { return Assembly.Load(assemblyName).GetType(typeName, true); }
        private static object Invoke(object target, string name, params object[] args) { return target.GetType().GetMethod(name).Invoke(target, args); }
        private static object Get(object target, string name) { return target.GetType().GetProperty(name).GetValue(target, null); }
    }
}
