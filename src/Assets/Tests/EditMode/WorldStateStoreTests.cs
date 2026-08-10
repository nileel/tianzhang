using System;
using System.Reflection;
using NUnit.Framework;

namespace TianZhang.Tests
{
    public sealed class WorldStateStoreTests
    {
        [Test]
        public void IndependentStoresCaptureAndRestoreTheirOwnState()
        {
            Type inventoryType = Load("TianZhang.World", "TianZhang.World.InventoryStore");
            object inventory = Activator.CreateInstance(inventoryType);
            Assert.That(Invoke(inventory, "TryGrant", "item_lingshi", 3), Is.EqualTo(true));
            object inventorySnapshot = Invoke(inventory, "Capture");
            object restoredInventory = Activator.CreateInstance(inventoryType);
            Invoke(restoredInventory, "Restore", inventorySnapshot);
            Assert.That(Invoke(restoredInventory, "GetQuantity", "item_lingshi"), Is.EqualTo(3));

            Type clockType = Load("TianZhang.World", "TianZhang.World.WorldClockService");
            object clock = Activator.CreateInstance(clockType, 7);
            Assert.That(Invoke(clock, "AdvanceDay"), Is.EqualTo(8));
            foreach (AssemblyName reference in clockType.Assembly.GetReferencedAssemblies())
                Assert.That(reference.Name, Is.Not.EqualTo("TianZhang.Gameplay"));
        }

        private static Type Load(string assemblyName, string typeName) { return Assembly.Load(assemblyName).GetType(typeName, true); }
        private static object Invoke(object target, string name, params object[] args) { return target.GetType().GetMethod(name).Invoke(target, args); }
    }
}
