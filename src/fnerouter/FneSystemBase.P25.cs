﻿/**
* Digital Voice Modem - Fixed Network Equipment
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Fixed Network Equipment
*
*/
/*
*   Copyright (C) 2022 by Bryan Biedenkapp N2PLL
*
*   This program is free software: you can redistribute it and/or modify
*   it under the terms of the GNU Affero General Public License as published by
*   the Free Software Foundation, either version 3 of the License, or
*   (at your option) any later version.
*
*   This program is distributed in the hope that it will be useful,
*   but WITHOUT ANY WARRANTY; without even the implied warranty of
*   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
*   GNU Affero General Public License for more details.
*/

using System;

using Serilog;

using fnecore;
using fnecore.DMR;
using fnecore.P25;
using System.Collections.Generic;

namespace fnerouter
{
    /// <summary>
    /// Implements a FNE system base.
    /// </summary>
    public abstract partial class FneSystemBase
    {
        /*
        ** Methods
        */

        /// <summary>
        /// Callback used to validate incoming P25 data.
        /// </summary>
        /// <param name="peerId">Peer ID</param>
        /// <param name="srcId">Source Address</param>
        /// <param name="dstId">Destination Address</param>
        /// <param name="callType">Call Type (Group or Private)</param>
        /// <param name="duid">P25 DUID</param>
        /// <param name="frameType">Frame Type</param>
        /// <param name="streamId">Stream ID</param>
        /// <param name="message">Raw message data</param>
        /// <returns>True, if data stream is valid, otherwise false.</returns>
        protected virtual bool P25DataValidate(uint peerId, uint srcId, uint dstId, CallType callType, P25DUID duid, FrameType frameType, uint streamId, byte[] message)
        {
            DateTime pktTime = DateTime.Now;

            SlotStatus status = new SlotStatus();
            if (p25Calls.ContainsKey(dstId)) 
                status = p25Calls[dstId];

            if (service.Blacklist != null)
            {
                if (service.Blacklist.Find((x) => x.Id == srcId) != null)
                {
                    if (streamId == status.RxStreamId)
                    {
                        // mark status variables for use later
                        status.RxStart = pktTime;
                        status.RxPeerId = peerId;
                        status.RxRFS = srcId;
                        status.RxType = frameType;
                        status.RxTGId = dstId;
                        status.RxStreamId = streamId;

                        Log.Logger.Warning($"({SystemName}) P25D: Traffic *REJECT ACL      * PEER {peerId} SRC_ID {srcId} DST_ID {dstId} DUID {duid} [STREAM ID {streamId}] (Blacklisted RID)");
                        // send report to monitor server
                        FneReporter.sendReport(new Dictionary<string, string> { { "SystemName", SystemName }, { "PEER", peerId.ToString() }, { "SRC_ID", srcId.ToString() }, { "DST_ID", dstId.ToString() }, { "DUID", duid.ToString() }, { "STREAM ID", streamId.ToString() }, { "Value", "CALL_REJECT_ACL" } });

                    }

                    return false;
                }
            }

            // always validate a TSDU or PDU if the source is valid
            if ((duid == P25DUID.TSDU) || (duid == P25DUID.PDU))
                return true;

            // always validate a terminator if the source is valid
            if ((duid == P25DUID.TDU) || (duid == P25DUID.TDULC))
            {
                bool grantDemand = ((message[14] & 0x80) == 0x80);

                if (dstId != 0 && rules.SendTgid && (activeTGIDs.Find((x) => x.Source.Tgid == dstId) == null))
                    return false;

                // is this a grant demand TDU?
                if (grantDemand)
                {
                    if (srcId == 0)
                        return false;
                    if (dstId == 0)
                        return false;

                    // perform peer ignored check for the destination
                    if (PeerIgnored(peerId, srcId, dstId, 0, callType, frameType, (frameType == FrameType.VOICE) ? DMRDataType.VOICE_LC_HEADER : DMRDataType.TERMINATOR_WITH_LC, streamId))
                        return false;
                }
            }

            if (callType == CallType.GROUP)
            {
                if (rules.SendTgid && (activeTGIDs.Find((x) => x.Source.Tgid == dstId) == null))
                {
                    if (streamId == status.RxStreamId)
                    {
                        // mark status variables for use later
                        status.RxStart = pktTime;
                        status.RxPeerId = peerId;
                        status.RxRFS = srcId;
                        status.RxType = frameType;
                        status.RxTGId = dstId;
                        status.RxStreamId = streamId;

                        Log.Logger.Warning($"({SystemName}) P25D: Traffic *REJECT ACL      * PEER {peerId} SRC_ID {srcId} DST_ID {dstId} [STREAM ID {streamId}] (Illegal TGID)");
    
                        // send report to monitor server
                        FneReporter.sendReport(new Dictionary<string,string> { {"SystemName",SystemName},{"PEER",peerId.ToString()},{"SRC_ID",srcId.ToString()},{"DST_ID",dstId.ToString()},{"STREAM ID",streamId.ToString()},{"Value","ILLEGAL_TGID"}});

                    }

                    return false;
                }
            }
            else if (callType == CallType.PRIVATE)
            {
                if (((service.Whitelist.Find((x) => x.Id == srcId) == null) && (service.Whitelist.Find((x) => x.Id == dstId) == null)) ||
                    ((service.Whitelist.Find((x) => x.Id == srcId) == null) || (service.Whitelist.Find((x) => x.Id == dstId) == null)))
                {
                    if (streamId == status.RxStreamId)
                    {
                        // mark status variables for use later
                        status.RxStart = pktTime;
                        status.RxPeerId = peerId;
                        status.RxRFS = srcId;
                        status.RxType = frameType;
                        status.RxTGId = dstId;
                        status.RxStreamId = streamId;

                        Log.Logger.Warning($"({SystemName}) P25D: Traffic *REJECT ACL      * PEER {peerId} SRC_ID {srcId} DST_ID {dstId} [STREAM ID {streamId}] (Illegal RID)");
    
                        // send report to monitor server
                        FneReporter.sendReport(new Dictionary<string,string> { {"SystemName",SystemName},{"PEER",peerId.ToString()},{"SRC_ID",srcId.ToString()},{"DST_ID",dstId.ToString()},{"STREAM ID",streamId.ToString()},{"Value","ILLEGAL_RID"}});
                    }

                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Event handler used to pre-process incoming P25 data.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected virtual void P25DataPreprocess(object sender, P25DataReceivedEvent e)
        {
            byte lcf = e.Data[4];

            // log but ignore TSDU and PDU packets here
            if ((e.DUID == P25DUID.TSDU) || (e.DUID == P25DUID.PDU))
            {
                if (e.DUID == P25DUID.TSDU)
                {
                    // handle the LCs
                    switch (lcf)
                    {
                        case P25Defines.TSBK_IOSP_GRP_AFF:
                            Log.Logger.Information($"({SystemName}) P25D: Traffic *TSBK GRP AFF    * PEER {e.PeerId} SRC_ID {e.SrcId} DST_ID {e.DstId} [STREAM ID {e.StreamId}]");
                            UpdateGroupAff(e.PeerId, e.SrcId, e.DstId, e.StreamId);
                            // send report to monitor server
                            FneReporter.sendReport(new Dictionary<string,string> { {"SystemName",SystemName},{"PEER",e.PeerId.ToString()},{"SRC_ID",e.SrcId.ToString()},{"DST_ID",e.DstId.ToString()},{"STREAM ID",e.StreamId.ToString()},{"Value","TSBK_IOSP_GRP_AFF"}});
                            break;
                        case P25Defines.TSBK_OSP_U_DEREG_ACK:
                            Log.Logger.Information($"({SystemName}) P25D: Traffic *TSBK U DEREG ACK* PEER {e.PeerId} SRC_ID {e.SrcId} [STREAM ID {e.StreamId}]");
                            RemoveGroupAff(e.PeerId, e.SrcId, e.StreamId);
                            // send report to monitor server
                            FneReporter.sendReport(new Dictionary<string,string> { {"SystemName",SystemName},{"PEER",e.PeerId.ToString()},{"SRC_ID",e.SrcId.ToString()},{"STREAM ID",e.StreamId.ToString()},{"Value","TSBK_OSP_U_DEREG_ACK"}});
                            break;
                        case P25Defines.TSBK_OSP_ADJ_STS_BCAST:
                            Log.Logger.Information($"({SystemName}) P25D: Traffic *TSBK ADJ STS BCS* PEER {e.PeerId} [STREAM ID {e.StreamId}]");
                            // send report to monitor server
                            FneReporter.sendReport(new Dictionary<string,string> { {"SystemName",SystemName},{"PEER",e.PeerId.ToString()},{"STREAM ID",e.StreamId.ToString()},{"Value","TSBK_OSP_ADJ_STS_BCAST"}});
                            break;
                        case P25Defines.TSBK_IOSP_CALL_ALRT:
                            Log.Logger.Information($"({SystemName}) P25D: Traffic *TSBK CALL ALERT * PEER {e.PeerId} SRC_ID {e.SrcId} DST_ID {e.DstId} [STREAM ID {e.StreamId}]");
                            // send report to monitor server
                            FneReporter.sendReport(new Dictionary<string,string> { {"SystemName",SystemName},{"PEER",e.PeerId.ToString()},{"SRC_ID",e.SrcId.ToString()},{"DST_ID",e.DstId.ToString()},{"STREAM ID",e.StreamId.ToString()},{"Value","TSBK_IOSP_CALL_ALRT"}});
                            break;
                        case P25Defines.TSBK_IOSP_ACK_RSP:
                            Log.Logger.Information($"({SystemName}) P25D: Traffic *TSBK ACK RSP    * PEER {e.PeerId} SRC_ID {e.SrcId} DST_ID {e.DstId} [STREAM ID {e.StreamId}]");
                            // send report to monitor server
                            FneReporter.sendReport(new Dictionary<string,string> { {"SystemName",SystemName},{"PEER",e.PeerId.ToString()},{"SRC_ID",e.SrcId.ToString()},{"DST_ID",e.DstId.ToString()},{"STREAM ID",e.StreamId.ToString()},{"Value","TSBK_IOSP_ACK_RSP"}});
                            break;
                    }
                }
                else if (e.DUID == P25DUID.PDU)
                {
                    Log.Logger.Information($"({SystemName}) P25D: Traffic *DATA            * PEER {e.PeerId} SRC_ID {e.SrcId} DST_ID {e.DstId} [STREAM ID {e.StreamId}]");
                    // send report to monitor server
                    FneReporter.sendReport(new Dictionary<string,string> { {"SystemName",SystemName},{"PEER",e.PeerId.ToString()},{"SRC_ID",e.SrcId.ToString()},{"DST_ID",e.DstId.ToString()},{"STREAM ID",e.StreamId.ToString()},{"Value","P25_DATA"}});
                }

                return;
            }

            return;
        }

        /// <summary>
        /// Event handler used to process incoming P25 data.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected virtual void P25DataReceived(object sender, P25DataReceivedEvent e)
        {
            DateTime pktTime = DateTime.Now;
            byte lco = e.Data[4];

            SlotStatus status = new SlotStatus();
            if (p25Calls.ContainsKey(e.DstId)) 
                status = p25Calls[e.DstId];

            // ignore TSDU or PDU packets here
            if ((e.DUID == P25DUID.TSDU) || (e.DUID == P25DUID.PDU))
                return;

            // override call type if necessary
            if (((e.DUID == P25DUID.TDU) || (e.DUID == P25DUID.TDULC)) && (status.RxType != FrameType.TERMINATOR))
            {
                if (status.RxCallType != e.CallType)
                    status.RxCallType = e.CallType;
            }

            if (e.CallType == CallType.GROUP)
            {
                // is this a new call stream?
                if (e.StreamId != status.RxStreamId && ((e.DUID != P25DUID.TDU) && (e.DUID != P25DUID.TDULC)))
                {
                    if ((status.RxType != FrameType.TERMINATOR) && (pktTime < status.RxTime.AddSeconds(Constants.STREAM_TO)) &&
                        (status.RxRFS != e.SrcId))
                    {
                        Log.Logger.Warning($"({SystemName}) P25D: Traffic *CALL COLLISION  * PEER {e.PeerId} SRC_ID {e.SrcId} TGID {e.DstId} [STREAM ID {e.StreamId}] (Collided with existing call)");
                        // send report to monitor server
                        FneReporter.sendReport(new Dictionary<string,string> { {"SystemName",SystemName},{"PEER",e.PeerId.ToString()},{"SRC_ID",e.SrcId.ToString()},{"TGID",e.DstId.ToString()},{"STREAM ID",e.StreamId.ToString()},{"Value","COLLISION"}});
                        return;
                    }

                    // this is a new call stream
                    status.RxPeerId = e.PeerId;
                    status.RxRFS = e.SrcId;
                    status.RxType = e.FrameType;
                    status.RxTGId = e.DstId;
                    status.RxTime = pktTime;
                    status.RxStreamId = e.StreamId;
                    status.RxStart = pktTime;

                    Log.Logger.Information($"({SystemName}) P25D: Traffic *CALL START      * PEER {e.PeerId} SRC_ID {e.SrcId} TGID {e.DstId} [STREAM ID {e.StreamId}]");
                    // send report to monitor server
                    FneReporter.sendReport(new Dictionary<string,string> { {"SystemName",SystemName},{"PEER",e.PeerId.ToString()},{"SRC_ID",e.SrcId.ToString()},{"TGID",e.DstId.ToString()},{"STREAM ID",e.StreamId.ToString()},{"Value","CALL_START"}});

                    status.RxCallType = CallType.GROUP;

                    if (p25Calls.ContainsKey(e.DstId)) 
                        p25Calls[e.DstId] = status;
                    else
                        p25Calls.Add(e.DstId, status);
                }

                // find the group voice rule by e.DstId, slot and whether or not the rule is active and routable
                RoutingRuleGroupVoice groupVoice = rules?.GroupVoice.Find((x) => x.Source.Tgid == e.DstId && x.Config.Active && x.Config.Routable);
                if (groupVoice != null)
                {
                    for (int i = 0; i < groupVoice.Destination.Count; i++)
                    {
                        RoutingRuleGroupVoiceDestination target = groupVoice.Destination[i];
                        FneSystemBase tgtSystem = service.Systems.Find((x) => x.SystemName.ToUpperInvariant() == target.Network.ToUpperInvariant());
                        if (tgtSystem != null)
                        {
                            if (tgtSystem.SystemName.ToUpperInvariant() == SystemName.ToUpperInvariant()) 
                            {
                                Log.Logger.Error($"({SystemName}) P25D: Call not routed, cowardly refusing to route a call to ourselves.");
                                continue;
                            }

                            SlotStatus tgtStatus = null;
                            if (tgtSystem.p25Calls.ContainsKey(target.Tgid)) 
                                tgtStatus = tgtSystem.p25Calls[target.Tgid];
                            if (tgtStatus == null) 
                                tgtStatus = new SlotStatus();

                            /*
                            ** Contention Handling
                            */

                            // from a different group than last RX from this system, but it has been less than Group Hangtime
                            if (tgtStatus.RxTGId != 0 && (target.Tgid != tgtStatus.RxTGId) && (pktTime - tgtStatus.RxTime < new TimeSpan(0, 0, rules.GroupHangTime)))
                            { 
                                Log.Logger.Information($"({SystemName}) P25D: Call not routed to TGID {target.Tgid}, target active or in group hangtime: PEER {tgtSystem.PeerId} TGID {tgtStatus.RxTGId}");
                                // send report to monitor server
                                FneReporter.sendReport(new Dictionary<string,string> { {"SystemName",SystemName},{"TARGET_TGID",tgtSystem.PeerId.ToString()},{"PEER",tgtSystem.PeerId.ToString()},{"TGID",tgtStatus.RxTGId.ToString()},{"Value","CALL_NOT_ROUTED_HANGTIME"}});
                                continue;
                            }

                            // from a different group than last TX to this system, but it has been less than Group Hangtime
                            if (tgtStatus.TxTGId != 0 && (target.Tgid != tgtStatus.TxTGId) && (pktTime - tgtStatus.TxTime < new TimeSpan(0, 0, rules.GroupHangTime)))
                            {
                                Log.Logger.Information($"({SystemName}) P25D: Call not routed to TGID {target.Tgid}, target in group hangtime: PEER {tgtSystem.PeerId} TGID {tgtStatus.TxTGId}");
                                // send report to monitor server
                                FneReporter.sendReport(new Dictionary<string,string> { {"SystemName",SystemName},{"TARGET_TGID",tgtSystem.PeerId.ToString()},{"PEER",tgtSystem.PeerId.ToString()},{"TGID",tgtStatus.TxTGId.ToString()},{"Value","CALL_NOT_ROUTED_HANGTIME"}});
                                continue;
                            }

                            // from the same group as the last RX from this system, but from a different subscriber, and it has been less than stream timeout
                            if (tgtStatus.RxTGId != 0 && tgtStatus.RxRFS != 0 && (target.Tgid != tgtStatus.RxTGId) && (e.SrcId != tgtStatus.RxRFS) && (pktTime - tgtStatus.RxTime < new TimeSpan(0, 0, 0, 0, (int)(Constants.STREAM_TO * 1000))))
                            {
                                Log.Logger.Information($"({SystemName}) P25D: Call not routed to TGID {target.Tgid}, matching call already active on target: PEER {tgtSystem.PeerId} TGID {tgtStatus.TxTGId} SRC_ID {tgtStatus.TxRFS}");
                                // send report to monitor server
                                FneReporter.sendReport(new Dictionary<string,string> { {"SystemName",SystemName},{"TARGET_TGID",tgtSystem.PeerId.ToString()},{"PEER",tgtSystem.PeerId.ToString()},{"TGID",tgtStatus.TxTGId.ToString()},{"SRC_ID",tgtStatus.TxRFS.ToString()},{"Value","CALL_NOT_ROUTED_CALLONTARGET"}});
                                continue;
                            }

                            // from the same group as the last TX to this system, but from a different subscriber, and it has been less than stream timeout
                            if (tgtStatus.TxTGId != 0 && tgtStatus.TxRFS != 0 && (target.Tgid != tgtStatus.TxTGId) && (e.SrcId != tgtStatus.TxRFS) && (pktTime - tgtStatus.RxTime < new TimeSpan(0, 0, 0, 0, (int)(Constants.STREAM_TO * 1000))))
                            {
                                Log.Logger.Information($"({SystemName}) P25D: Call not routed to TGID {target.Tgid}, call route in progress on target: PEER {tgtSystem.PeerId} TGID {tgtStatus.TxTGId} SRC_ID {tgtStatus.TxRFS}");
                                // send report to monitor server
                                FneReporter.sendReport(new Dictionary<string,string> { {"SystemName",SystemName},{"TARGET_TGID",tgtSystem.PeerId.ToString()},{"PEER",tgtSystem.PeerId.ToString()},{"TGID",tgtStatus.TxTGId.ToString()},{"SRC_ID",tgtStatus.TxRFS.ToString()},{"STREAM ID",e.StreamId.ToString()},{"Value","CALL_NOT_ROUTED_CALLINPROGRESS"}});
                                continue;
                            }

                            // set values for the contention handler to test next time
                            tgtStatus.TxTime = pktTime;

                            if ((e.StreamId != status.RxStreamId) || (tgtStatus.TxRFS != e.SrcId) || (tgtStatus.TxTGId != target.Tgid))
                            {
                                // record the destination TGID and stream ID
                                tgtStatus.TxTGId = target.Tgid;
                                tgtStatus.TxPITGId = 0;
                                tgtStatus.TxStreamId = e.StreamId;
                                tgtStatus.TxRFS = e.SrcId;

                                if (tgtSystem.p25Calls.ContainsKey(target.Tgid))
                                    tgtSystem.p25Calls[target.Tgid] = status;
                                else
                                    tgtSystem.p25Calls.Add(target.Tgid, status);

                                Log.Logger.Information($"({SystemName}) P25D: Call routed to SYSTEM {target.Network} TGID {target.Tgid}");
                                // send report to monitor server
                                FneReporter.sendReport(new Dictionary<string,string> { {"SystemName",SystemName},{"SYSTEM",target.Network.ToString()},{"TGID",target.Tgid.ToString()},{"Value","CALL_ROUTED"}});
                            }

                            byte[] frame = new byte[e.Data.Length];
                            Buffer.BlockCopy(e.Data, 0, frame, 0, e.Data.Length);

                            // re-write destination TGID in the frame
                            frame[8] = (byte)((target.Tgid >> 16) & 0xFF);
                            frame[9] = (byte)((target.Tgid >> 8) & 0xFF);
                            frame[10] = (byte)((target.Tgid >> 0) & 0xFF);

                            // what type of FNE are we?
                            if (tgtSystem.FneType == FneType.MASTER)
                            {
                                FneMaster master = (FneMaster)tgtSystem.fne;
                                foreach (uint peerId in master.Peers.Keys)
                                    master.SendPeer(peerId, FneBase.CreateOpcode(Constants.NET_FUNC_PROTOCOL, Constants.NET_PROTOCOL_SUBFUNC_P25), frame, e.PacketSequence, e.StreamId);
                            }
                            else if (tgtSystem.FneType == FneType.PEER)
                            {
                                FnePeer peer = (FnePeer)tgtSystem.fne;
                                peer.SendMaster(FneBase.CreateOpcode(Constants.NET_FUNC_PROTOCOL, Constants.NET_PROTOCOL_SUBFUNC_P25), frame, e.PacketSequence, e.StreamId);
                            }

                            Log.Logger.Debug($"({SystemName}) P25 Packet routed by rule {groupVoice.Name} to SYSTEM {tgtSystem.SystemName}");
                        }
                    }
                }

                // final actions - is this a voice terminator?
                if (((e.DUID == P25DUID.TDU) || (e.DUID == P25DUID.TDULC)) && (status.RxType != FrameType.TERMINATOR))
                {
                    TimeSpan callDuration = pktTime - status.RxStart;
                    Log.Logger.Information($"({SystemName}) P25D: Traffic *CALL END        * PEER {e.PeerId} SRC_ID {e.SrcId} TGID {e.DstId} DUR {callDuration.TotalSeconds} [STREAM ID: {e.StreamId}]");
                    // send report to monitor server
                    FneReporter.sendReport(new Dictionary<string,string> { {"SystemName",SystemName},{"PEER",e.PeerId.ToString()},{"SRC_ID",e.SrcId.ToString()},{"TGID",e.DstId.ToString()},{"DUR",callDuration.TotalSeconds.ToString()},{"STREAM ID",e.StreamId.ToString()},{"Value","CALL_END"}});

                    if (p25Calls.ContainsKey(e.DstId))
                        p25Calls.Remove(e.DstId);
                }

                status.RxPeerId = e.PeerId;
                status.RxRFS = e.SrcId;
                status.RxType = e.FrameType;
                status.RxTGId = e.DstId;
                status.RxTime = pktTime;
                status.RxStreamId = e.StreamId;

                if (p25Calls.ContainsKey(e.DstId))
                    p25Calls[e.DstId] = status;
            }
            else if (e.CallType == CallType.PRIVATE)
            {
                // is this a new call stream?
                if (e.StreamId != status.RxStreamId)
                {
                    if ((status.RxType != FrameType.TERMINATOR) && (pktTime < status.RxTime.AddSeconds(Constants.STREAM_TO)) &&
                        (status.RxRFS != e.SrcId))
                    {
                        Log.Logger.Warning($"({SystemName}) P25D: Traffic *CALL COLLISION  * PEER {e.PeerId} SRC_ID {e.SrcId} DST_ID {e.DstId} [STREAM ID {e.StreamId}] (Collided with existing call)");
                        // send report to monitor server
                        FneReporter.sendReport(new Dictionary<string,string> { {"SystemName",SystemName},{"PEER",e.PeerId.ToString()},{"SRC_ID",e.SrcId.ToString()},{"DST_ID",e.DstId.ToString()},{"STREAM ID",e.StreamId.ToString()},{"Value","CALL_COLLISON"}});
                        return;
                    }

                    // this is a new call stream
                    status.RxPeerId = e.PeerId;
                    status.RxRFS = e.SrcId;
                    status.RxType = e.FrameType;
                    status.RxTGId = e.DstId;
                    status.RxTime = pktTime;
                    status.RxStreamId = e.StreamId;
                    status.RxStart = pktTime;

                    Log.Logger.Information($"({SystemName}) P25D: Traffic *PRV CALL START  * PEER {e.PeerId} SRC_ID {e.SrcId} DST_ID {e.DstId} [STREAM ID {e.StreamId}]");
                    // send report to monitor server
                    FneReporter.sendReport(new Dictionary<string,string> { {"SystemName",SystemName},{"PEER",e.PeerId.ToString()},{"SRC_ID",e.SrcId.ToString()},{"DST_ID",e.DstId.ToString()},{"STREAM ID",e.StreamId.ToString()},{"Value","PRIVATE_CALL_START"}});

                    status.RxCallType = CallType.PRIVATE;

                    if (p25Calls.ContainsKey(e.DstId))
                        p25Calls[e.DstId] = status;
                    else
                        p25Calls.Add(e.DstId, status);
                }

                // final actions - is this a voice terminator?
                if (((e.DUID == P25DUID.TDU) || (e.DUID == P25DUID.TDULC)) && (status.RxType != FrameType.TERMINATOR))
                {
                    TimeSpan callDuration = pktTime - status.RxStart;
                    Log.Logger.Information($"({SystemName}) P25D: Traffic *PRV CALL END    * PEER {e.PeerId} SRC_ID {e.SrcId} DST_ID {e.DstId} DUR {callDuration.TotalSeconds} [STREAM ID: {e.StreamId}]");
                    // send report to monitor server
                    FneReporter.sendReport(new Dictionary<string,string> { {"SystemName",SystemName},{"PEER",e.PeerId.ToString()},{"SRC_ID",e.SrcId.ToString()},{"DST_ID",e.DstId.ToString()},{"DUR",callDuration.TotalSeconds.ToString()},{"STREAM ID",e.StreamId.ToString()},{"Value","PRIVATE_CALL_END"}});
                
                    if (p25Calls.ContainsKey(e.DstId))
                        p25Calls.Remove(e.DstId);
                }

                status.RxPeerId = e.PeerId;
                status.RxRFS = e.SrcId;
                status.RxType = e.FrameType;
                status.RxTGId = e.DstId;
                status.RxTime = pktTime;
                status.RxStreamId = e.StreamId;

                if (p25Calls.ContainsKey(e.DstId))
                    p25Calls[e.DstId] = status;
            }
        }
    } // public abstract partial class FneSystemBase
} // namespace fnerouter
