﻿using log4net;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UberStrok.Core;
using UberStrok.Core.Views;

namespace UberStrok.Realtime.Server.Comm
{
    public partial class LobbyRoom : BaseLobbyRoomOperationHandler, IRoom<CommPeer>, IDisposable
    {
        private static readonly ILog Log = LogManager.GetLogger(nameof(LobbyRoom));

        private bool _disposed;
        private readonly Loop _loop;
        private readonly List<CommPeer> _peers;

        private readonly List<CommPeer> _failedPeers;

        public object Sync { get; }
        public IReadOnlyList<CommPeer> Peers { get; }

        public LobbyRoom()
        {
            _loop = new Loop(5);
            _peers = new List<CommPeer>();
            _failedPeers = new List<CommPeer>();

            Sync = new object();
            Peers = _peers.AsReadOnly();

            /* Start lobby room loop. */
            _loop.Start(OnTick, OnTickError);
        }

        public void Join(CommPeer peer)
        {
            if (peer == null)
                throw new ArgumentNullException(nameof(peer));

            Debug.Assert(peer.Room == null, "CommPeer is joining room, but its already in another room.");

            peer.Room = this;

            CommPeer oldPeer;
            lock (Sync)
                oldPeer = Find(peer.Actor.Cmid);

            if (oldPeer != null)
                Leave(oldPeer);

            lock (Sync)
                _peers.Add(peer);

            Log.Debug($"CommPeer joined the room {peer.Actor.Cmid}");

            peer.Handlers.Add(this);
            peer.Events.SendLobbyEntered();

            UpdateList();
        }

        public void Leave(CommPeer peer)
        {
            if (peer == null)
                throw new ArgumentNullException(nameof(peer));

            lock (Sync)
                _peers.Remove(peer);

            Log.Debug($"CommPeer left the room {peer.Actor.Cmid}");

            peer.Handlers.Remove(Id);
            UpdateList();
            /* TODO: Tell the web servers to close the user's session or something. */
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _loop.Dispose();
            _disposed = true;
        }

        private void OnTick()
        {
            _failedPeers.Clear();

            lock (Sync)
            {
                foreach (var peer in Peers)
                {
                    if (peer.HasError)
                    {
                        peer.Disconnect();
                        break;
                    }

                    try { peer.Update(); }
                    catch (Exception ex)
                    {
                        /* NOTE: This should never happen, but just incase. */
                        Log.Error("Failed to update peer.", ex);
                        _failedPeers.Add(peer);
                    }
                }
            }

            foreach (var peer in _failedPeers)
                Leave(peer);
        }

        private void OnTickError(Exception ex)
        {
            Log.Error("Failed to tick.", ex);
        }

        /* Update all peer's player list in the lobby. */
        private void UpdateList()
        {
            lock (Sync)
            {
                var actors = new List<CommActorInfoView>(_peers.Count);
                for (int i = 0; i < _peers.Count; i++)
                    actors.Add(_peers[i].Actor.View);

                for (int i = 0; i < _peers.Count; i++)
                    _peers[i].Events.Lobby.SendFullPlayerListUpdate(actors);
            }
        }

        private CommPeer Find(int cmid)
        {
            lock (Sync)
            {
                for (int i = 0; i < _peers.Count; i++)
                {
                    var peer = _peers[i];
                    if (peer.Actor.Cmid == cmid)
                        return peer;
                }
            }

            return null;
        }

        private static bool IsSpeedHacking(List<float> td)
        {
            float mean = 0;
            for (int i = 0; i < td.Count; i++)
                mean += td[i];

            mean /= td.Count;
            if (mean > 2f)
                return true;

            float variance = 0;
            for (int i = 0; i < td.Count; i++)
                variance += (float)Math.Pow(td[i] - mean, 2);

            variance /= td.Count - 1;
            return mean > 1.1f && variance <= 0.05f;
        }
    }
}
