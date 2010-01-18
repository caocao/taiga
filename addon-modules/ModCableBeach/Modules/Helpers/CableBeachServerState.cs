﻿/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.Text;
using log4net;
using OpenSim.Framework.Servers.HttpServer;
using OpenMetaverse;

using CapabilityIdentifier = System.Uri;
using ServiceIdentifier = System.Uri;

namespace ModCableBeach
{
    public delegate void CreateCapabilitiesCallback(Uri requestUrl, UUID sessionID, Uri identity, ref Dictionary<Uri, Uri> capabilities);

    class Capability
    {
        public UUID CapabilityID;
        public UUID SessionID;
        public BaseStreamHandler HttpHandler;
        public bool ClientCertRequired;
        public object State;

        public Capability(UUID capabilityID, UUID sessionID, BaseStreamHandler httpHandler, bool clientCertRequired, object state)
        {
            CapabilityID = capabilityID;
            SessionID = sessionID;
            HttpHandler = httpHandler;
            ClientCertRequired = clientCertRequired;
            State = state;
        }
    }

    public static class CableBeachServerState
    {
        #region Constants

        /// <summary>Base path for capabilities generated by Cable Beach</summary>
        public const string CABLE_BEACH_CAPS_PATH = "/caps/cablebeach/";

        /// <summary>Number of minutes a capability can go unused before timing out. Default is 12 hours</summary>
        const int CAPABILITY_TIMEOUT_MINUTES = 60 * 12;

        const string ROOT_PAGE_TEMPLATE_FILE = "webtemplates/inventoryserver_rootpage.tpl";

        #endregion Constants

        /// <summary>Shared logger for Cable Beach server side components</summary>
        public static readonly ILog Log = LogManager.GetLogger("CableBeachServer");

        /// <summary>Template engine for rendering dynamic webpages</summary>
        public static readonly SmartyEngine WebTemplates = new SmartyEngine();

        /// <summary>Holds active capabilities, mapping from capability UUID to
        /// callback and session information</summary>
        public static ExpiringCache<UUID, Capability> Capabilities = new ExpiringCache<UUID, Capability>();

        /// <summary>Holds callbacks for each registered Cable Beach service to
        /// create capabilities on an incoming capability request</summary>
        private static Dictionary<Uri, CreateCapabilitiesCallback> m_serviceCallbacks = new Dictionary<Uri, CreateCapabilitiesCallback>();

        #region Capabilities

        public static void RegisterService(ServiceIdentifier serviceIdentifier, CreateCapabilitiesCallback capabilitiesCallback)
        {
            lock (m_serviceCallbacks)
                m_serviceCallbacks[serviceIdentifier] = capabilitiesCallback;
        }

        public static Uri CreateCapability(Uri requestUrl, UUID sessionID, BaseStreamHandler httpHandler, bool clientCertRequired, object state)
        {
            UUID capID = UUID.Random();

            Capabilities.AddOrUpdate(
                capID,
                new Capability(capID, sessionID, httpHandler, clientCertRequired, state),
                TimeSpan.FromHours(CAPABILITY_TIMEOUT_MINUTES));

            return new Uri(requestUrl, CABLE_BEACH_CAPS_PATH + capID.ToString());
        }

        public static bool RemoveCapabilities(UUID sessionID)
        {
            // TODO: Rework how capabilities are stored so we can remove all of the caps with a given sessionID
            return false;
        }

        /// <summary>
        /// Called when an incoming request_capabilities message is received. Loops through all of the
        /// registered Cable Beach services and gives them a chance to create capabilities for each
        /// request in the dictionary
        /// </summary>
        /// <param name="requestUrl"></param>
        /// <param name="identity"></param>
        /// <param name="capabilities"></param>
        public static void CreateCapabilities(Uri requestUrl, Uri identity, ref Dictionary<CapabilityIdentifier, Uri> capabilities)
        {
            UUID sessionID = UUID.Random();

            lock (m_serviceCallbacks)
            {
                foreach (KeyValuePair<ServiceIdentifier, CreateCapabilitiesCallback> entry in m_serviceCallbacks)
                {
                    try { entry.Value(requestUrl, sessionID, identity, ref capabilities); }
                    catch (Exception ex)
                    {
                        Log.Error("[CABLE BEACH SERVER]: Service " + entry.Key +
                            " threw an exception during capability creation: " + ex.Message, ex);
                    }
                }
            }
        }

        #endregion Capabilities

        #region HTML Templates

        public static byte[] BuildInventoryRootPageTemplate(string xrdUrl)
        {
            string output = null;
            Dictionary<string, object> variables = new Dictionary<string, object>();
            variables["xrd_url"] = xrdUrl;

            try { output = WebTemplates.Render(ROOT_PAGE_TEMPLATE_FILE, variables); }
            catch (Exception) { }
            if (output == null) { output = "Failed to render template " + ROOT_PAGE_TEMPLATE_FILE; }

            return Encoding.UTF8.GetBytes(output);
        }

        #endregion HTML Templates
    }
}
