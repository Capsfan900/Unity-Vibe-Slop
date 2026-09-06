// Re-arms the MCP bridge after a domain reload when it is down.
//
// McpBootstrap runs ONCE per editor session. If that one attempt races the server -- it did on
// 2026-09-04: the HTTP transport gave up 40 s before the uvx server finished starting -- the bridge
// stays down until a human clicks Tools/MCP Bootstrap, and every session driving the editor from
// Claude Code sees `no_unity_session`. The editor reloads its domain on every script change, so
// retrying here means the next compile heals the bridge on its own. Idempotent: it does nothing while
// the bridge is up, and it never starts the server (that stays McpBootstrap's job).
using System;
using System.Threading.Tasks;
using MCPForUnity.Editor.Services;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class McpReconnect
{
    static McpReconnect()
    {
        EditorApplication.delayCall += () => _ = RunAsync();
    }

    [MenuItem("Tools/MCP Bootstrap/Reconnect Bridge")]
    public static void RunFromMenu() => _ = RunAsync();

    static async Task RunAsync()
    {
        try
        {
            var bridge = MCPServiceLocator.Bridge;
            if (bridge.IsRunning) return;

            var server = MCPServiceLocator.Server;
            if (!server.IsLocalHttpServerReachable())
            {
                Debug.LogWarning("[McpReconnect] bridge is down and the local HTTP server is not reachable; " +
                                 "run Tools/MCP Bootstrap/Start Server on 8090.");
                return;
            }

            bool ok = await bridge.StartAsync();
            Debug.Log(ok ? "[McpReconnect] BRIDGE CONNECTED" : "[McpReconnect] bridge StartAsync returned false");
        }
        catch (Exception e) { Debug.LogError("[McpReconnect] " + e); }
    }
}
