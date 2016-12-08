using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using ConDep.Dsl.Config;
using ConDep.Dsl.Logging;
using SnmpSharpNet;

namespace ConDep.Dsl.LoadBalancer.AlohaHaProxy
{
    public class HaProxyLoadBalancer : ILoadBalance
    {
        private readonly LoadBalancerConfig _config;
        private readonly string _scope;
        private readonly string _snmpEndpoint;
        private readonly int _snmpPort;
        private readonly string _snmpCommunity;
        private readonly int _waitTimeInSecondsAfterSettingServerStateToOffline;
        private readonly int _waitTimeInSecondsAfterSettingServerStateToOnline;

        private enum ServerState
        {
            Online,
            Offline
        }

        LoadBalancerMode ILoadBalance.Mode { get; set; }

        public HaProxyLoadBalancer(LoadBalancerConfig config)
        {
            _config = config;

            if (config.CustomConfig == null)
                throw new ConDepLoadBalancerException("No config found in section CustomConfig under load balancer. HaProxy requires CustomConfig for at least SnmpEndpoint.");

            if (config.CustomConfig.SnmpEndpoint == null)
                throw new ConDepLoadBalancerException("No SnmpEndpoint exist in configuration. Please add this under CustomConfig.");

             _snmpEndpoint = config.CustomConfig.SnmpEndpoint;
            _scope = config.CustomConfig.Scope ?? "root";
            _snmpPort = config.CustomConfig.SnmpPort ?? 161;
            _snmpCommunity = config.CustomConfig.SnmpCommunity ?? "public";

            _waitTimeInSecondsAfterSettingServerStateToOffline = config.CustomConfig.WaitTimeInSecondsAfterSettingServerStateToOffline ?? config.CustomConfig.WaitTimeInSecondsAfterMaintenanceModeChanged ?? 5;
            _waitTimeInSecondsAfterSettingServerStateToOnline = config.CustomConfig.WaitTimeInSecondsAfterSettingServerStateToOnline ?? config.CustomConfig.WaitTimeInSecondsAfterMaintenanceModeChanged ?? 5;
        }

        public Result BringOffline(string serverName, string farm, LoadBalancerSuspendMethod suspendMethod)
        {
            var result = ChangeServerState(serverName, farm, ServerState.Offline);

            if (!result.IsSuccessStatusCode)
                throw new ConDepLoadBalancerException(string.Format("Failed to take server {0} offline in loadbalancer. Returned status code was {1} with reason: {2}", serverName, result.StatusCode, result.ReasonPhrase));

            Logger.Verbose(string.Format("Waiting {0} seconds to give load balancer a chance to set server i maintenance mode.", _waitTimeInSecondsAfterSettingServerStateToOffline));
            Thread.Sleep(_waitTimeInSecondsAfterSettingServerStateToOffline * 1000);

            Logger.Verbose("Waiting for server connections to drain.");
            WaitForCurrentConnectionsToDrain(farm, serverName, _snmpEndpoint, _snmpPort, _snmpCommunity, DateTime.Now.AddSeconds(_config.TimeoutInSeconds));
            return new Result(true, false);
        }

        public Result BringOnline(string serverName, string farm)
        {
            var result = ChangeServerState(serverName, farm, ServerState.Online);

            if (!result.IsSuccessStatusCode)
                throw new ConDepLoadBalancerException(string.Format("Failed to take server {0} online in loadbalancer. Returned status code was {1} with reason: {2}", serverName, result.StatusCode, result.ReasonPhrase));

            Logger.Verbose(string.Format("Waiting {0} seconds to give load balancer a chance to get server out of maintenance mode.", _waitTimeInSecondsAfterSettingServerStateToOnline));
            Thread.Sleep(_waitTimeInSecondsAfterSettingServerStateToOnline * 1000);
            return new Result(true, false);
        }

        private static string GetServerStateCommand(ServerState serverState)
        {
            switch (serverState)
            {
                case ServerState.Offline:
                    return "\"enabled\"";
                case ServerState.Online:
                    return "null";
            }

            throw new ConDepLoadBalancerException("Server state for command unknown");
        }

        private HttpResponseMessage ChangeServerState(string serverName, string farm, ServerState serverState)
        {
            var cmd = GetServerStateCommand(serverState);
            var client = new HttpClient {BaseAddress = new Uri(_config.Name)};
            Logger.Verbose("Connecting to load balancer using " + client.BaseAddress);

            var request = new HttpRequestMessage(HttpMethod.Put,_config.Name + "/api/2/scope/" + _scope + "/l7/farm/" + farm + "/server/" + serverName);
            Logger.Verbose("Executing PUT command to " + request.RequestUri);

            request.Headers.Accept.Clear();
            request.Headers.Add("Authorization", "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes(string.Format("{0}:{1}", _config.UserName, _config.Password))));
            request.Content = new StringContent(string.Format("{{ \"maintenance\" : {0} }}", cmd), Encoding.ASCII, "application/json");

            var result = client.SendAsync(request).Result;

            return result;
        }

        private const string FARM_NAMES_OID          = ".1.3.6.1.4.1.23263.4.2.1.3.3.1.3";
        private const string SERVER_NAMES_OID        = ".1.3.6.1.4.1.23263.4.2.1.3.4.1.4.1.";
        private const string SERVER_CUR_SESSIONS_OID = ".1.3.6.1.4.1.23263.4.2.1.3.4.1.8.1.";

        private void WaitForCurrentConnectionsToDrain(string farm, string server, string snmpEndpoint, int snmpPort, string snmpCommunity, DateTime timeout)
        {
            if (DateTime.Now > timeout)
                throw new ConDepLoadBalancerException(string.Format("Timed out while taking {0} offline in load balancer.", server));

            Logger.Verbose("Connecting to snmp using " + snmpEndpoint + " on port " + snmpPort + " with community " + snmpCommunity);
            var snmp = new SimpleSnmp(snmpEndpoint, snmpPort, snmpCommunity);
            
            Logger.Verbose("Getting snmp info about farm " + farm);
            var farmResult = snmp.Walk(SnmpVersion.Ver2, FARM_NAMES_OID);
            var farmOid = farmResult.Single(x => x.Value.Clone().ToString() == farm);

            var id = farmOid.Key.ToString();
            var start = farmOid.Key.ToString().LastIndexOf(".");
            var farmSubId = id.Substring(start + 1, id.Length - start - 1);

            Logger.Verbose("Getting snmp info about server " + server);
            var serverResult = snmp.Walk(SnmpVersion.Ver2, SERVER_NAMES_OID + farmSubId);
            var serverOid = serverResult.Single(x => x.Value.Clone().ToString() == server);

            var serverId = serverOid.Key.ToString();
            start = serverOid.Key.ToString().LastIndexOf(".");
            var serverSubId = serverId.Substring(start + 1, serverId.Length - start - 1);

            Logger.Verbose("Getting current server sessions for server " + server);
            var pdu = Pdu.GetPdu();
            pdu.VbList.Add(SERVER_CUR_SESSIONS_OID + farmSubId + "." + serverSubId);
            var currentSessionsVal = snmp.Get(SnmpVersion.Ver2, pdu);
            var val = currentSessionsVal.Single().Value.Clone() as Counter64;

            if (val > 0)
            {
                Logger.Verbose("Server " + server + " has " + val + " active sessions.");
                var waitInterval = 3;
                Logger.Warn(string.Format("There are still {0} active connections on server {1}. Waiting {2} second before retry.", val, server, waitInterval));
                Thread.Sleep(1000 * waitInterval);
                WaitForCurrentConnectionsToDrain(farm, server, snmpEndpoint, snmpPort, snmpCommunity, timeout);
            }

            Logger.Verbose("Server " + server + " has 0 active session and is now offline.");
        }
    }
}
