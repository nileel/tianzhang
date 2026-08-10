using System;
using System.Reflection;
using NUnit.Framework;

namespace TianZhang.Tests
{
    public sealed class CharterCommitServiceTests
    {
        [Test]
        public void CommitServiceRejectsUnknownAndIllegalAtomsAndCommitsOnlyOnce()
        {
            Assembly assembly = Assembly.Load("TianZhang.World");
            Type definitionType = assembly.GetType("TianZhang.World.CharterDefinition", true);
            Type storeType = assembly.GetType("TianZhang.World.CharterStore", true);
            Type authorizationType = assembly.GetType("TianZhang.World.CharterAuthorization", true);
            Type requestType = assembly.GetType("TianZhang.World.CharterInvocationRequest", true);
            Type serviceType = assembly.GetType("TianZhang.World.CharterCommitService", true);
            object store = Activator.CreateInstance(storeType);
            object authorization = Activator.CreateInstance(authorizationType);
            object service = Activator.CreateInstance(serviceType);
            object definition = Activator.CreateInstance(definitionType, "entry_suifu", "auth_suifu", "site_old_water", new[] { "atom_open" });
            Assert.That(Invoke(store, "RegisterDefinition", definition), Is.EqualTo(true));
            Assert.That(Invoke(authorization, "Grant", "auth_suifu"), Is.EqualTo(true));

            object unknown = Activator.CreateInstance(requestType, "missing", "auth_suifu", new[] { "atom_open" });
            Assert.That(Get(Invoke(service, "TryCommit", store, authorization, unknown), "Succeeded"), Is.EqualTo(false));
            object illegal = Activator.CreateInstance(requestType, "entry_suifu", "auth_suifu", new[] { "atom_open", "atom_extra" });
            Assert.That(Get(Invoke(service, "TryCommit", store, authorization, illegal), "Reason"), Is.EqualTo("ILLEGAL_ATOMIC_COMMIT"));

            object valid = Activator.CreateInstance(requestType, "entry_suifu", "auth_suifu", new[] { "atom_open" });
            Assert.That(Get(Invoke(service, "TryCommit", store, authorization, valid), "Succeeded"), Is.EqualTo(true));
            Assert.That(Get(Invoke(service, "TryCommit", store, authorization, valid), "Reason"), Is.EqualTo("CONFLICT"));
        }

        private static object Invoke(object target, string name, params object[] args) { return target.GetType().GetMethod(name).Invoke(target, args); }
        private static object Get(object target, string name) { return target.GetType().GetProperty(name).GetValue(target, null); }
    }
}
