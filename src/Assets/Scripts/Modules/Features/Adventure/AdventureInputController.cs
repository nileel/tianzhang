using System;
using UnityEngine;

namespace TianZhang.Features.Adventure
{
    public sealed class AdventureInputController : MonoBehaviour
    {
        private AdventureSession session;
        private AdventureNodeDispatcher dispatcher;
        private AdventureHudPresenter hud;

        public void Configure(
            AdventureSession adventureSession,
            AdventureNodeDispatcher nodeDispatcher,
            AdventureHudPresenter presenter)
        {
            session = adventureSession ?? throw new ArgumentNullException(nameof(adventureSession));
            dispatcher = nodeDispatcher ?? throw new ArgumentNullException(nameof(nodeDispatcher));
            hud = presenter;
        }

        public bool SelectNode(string nodeId)
        {
            if (session == null || !session.TryGetNode(nodeId, out var node)) return false;
            bool handled = dispatcher.TryDispatch(node, out string reason);
            if (handled) session.Select(node, reason);
            hud?.Present(session, SelectNode, handled ? null : reason);
            return handled;
        }
    }
}
