﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog.Config;
using NLog.Layouts;
using Microsoft.AspNet.SignalR.Client;

namespace NLog.Targets.NetworkJSON
{
    [Target("GDService")]
    public class GDServiceTarget : TargetWithLayout
    {
        #region NetworkJson Reliability Service Variables

        private HubConnection _localHubConnection;
        private IHubProxy _localHubProxy;
        private Uri _guaranteedDeliveryEndpoint;
        private Uri _networkJsonEndpoint;

        #endregion

        #region Task Properties

        [Required]
        public string GuaranteedDeliveryEndpoint
        {
            get { return _guaranteedDeliveryEndpoint.ToString(); }
            set
            {
                if (value != null)
                {
                    _guaranteedDeliveryEndpoint = new Uri(Environment.ExpandEnvironmentVariables(value));
                    ClearHubConnection();
                    _localHubConnection = new HubConnection(GuaranteedDeliveryEndpoint);
                    _localHubProxy = _localHubConnection.CreateHubProxy("GDServiceLogger");

                    _localHubConnection.Start().Wait();
                }
                else
                {
                    _guaranteedDeliveryEndpoint = null;
                    ClearHubConnection();
                }
            }
        }

        [Required]
        public string NetworkJsonEndpoint
        {
            get { return _networkJsonEndpoint.ToString(); }
            set
            {
                if (value != null)
                {
                    _networkJsonEndpoint = new Uri(Environment.ExpandEnvironmentVariables(value));
                }
                else
                {
                    _networkJsonEndpoint = null;
                }
            }
        }

        private void ClearHubConnection()
        {
            if (_localHubConnection != null)
            {
                _localHubConnection.Stop();
                _localHubConnection.Dispose();
                _localHubConnection = null;
                _localHubProxy = null;
            }
        }

        [ArrayParameter(typeof(ParameterInfo), "parameter")]
        public IList<ParameterInfo> Parameters { get; }
        
        #endregion

        private IConverter Converter { get; }

        public GDServiceTarget() : this(new JsonConverter())
        {
        }

        public GDServiceTarget(IConverter converter)
        {
            Converter = converter;
            this.Parameters = new List<ParameterInfo>();
        }
        
        public void WriteLogEventInfo(LogEventInfo logEvent)
        {
            Write(logEvent);
        }

        protected override async void Write(LogEventInfo logEvent)
        {
            foreach (var par in this.Parameters)
            {
                if (!logEvent.Properties.ContainsKey(par.Name))
                {
                    string stringValue = par.Layout.Render(logEvent);

                    logEvent.Properties.Add(par.Name, stringValue);
                }
            }
            
            var jsonObject = Converter.GetLogEventJson(logEvent);
            if (jsonObject == null) return;
            var jsonObjectStr = jsonObject.ToString(Formatting.None, null);

            await WriteAsync(jsonObjectStr);
        }

        /// <summary>
        /// Exposed for unit testing and load testing purposes.
        /// </summary>
        public Task WriteAsync(string logEventAsJsonString)
        {
            if (_localHubConnection == null)
            {
                throw new HubException($"Connection to {_guaranteedDeliveryEndpoint} not initialized");
            }
            if(_localHubConnection.State != ConnectionState.Connected)
            {
                throw new HubException($"Connection to {_guaranteedDeliveryEndpoint} not online");
            }
            return _localHubProxy.Invoke("storeAndForward", NetworkJsonEndpoint, logEventAsJsonString);
        }
    }
}
