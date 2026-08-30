// One-shot bootstrap: port 8080 is taken by Windows (iphlpsvc), so move the MCP
// HTTP server to 8090, enable auto-start, and bring the bridge up. Also exposes
// a menu item to re-run it. Safe to delete once the bridge is stable.
using System;
using System.Threading.Tasks;
using MCPForUnity.Editor.Services;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class McpBootstrap
{
    const string Url = "http://127.0.0.1:8090/mcp";
    const string DoneKey = "vibegame1.McpBootstrap.Done";

    static McpBootstrap()
    {
        if (SessionState.GetBool(DoneKey, false)) return;
        SessionState.SetBool(DoneKey, true);
        EditorApplication.delayCall += () => _ = RunAsync();
    }

    [MenuItem("Tools/MCP Bootstrap/Start Server on 8090")]
    public static void RunFromMenu() => _ = RunAsync();

    static async Task RunAsync()
    {
        try
        {
            var cfg = EditorConfigurationCache.Instance;
            cfg.SetUseHttpTransport(true);
            cfg.SetHttpBaseUrl(Url);
            EditorPrefs.SetBool("MCPForUnity.AutoStartOnLoad", true);

            var server = MCPServiceLocator.Server;
            if (!server.IsLocalHttpServerReachable())
            {
                Debug.Log("[McpBootstrap] starting local HTTP server on " + Url);
                if (!server.StartLocalHttpServer(quiet: true))
                {
                    Debug.LogError("[McpBootstrap] StartLocalHttpServer returned false; see " + server.GetLocalHttpServerLaunchLogPath());
                    server.LogLocalHttpServerLaunchFailure();
                    return;
                }
            }

            double t0 = EditorApplication.timeSinceStartup;
            while (!server.IsLocalHttpServerReachable())
            {
                if (EditorApplication.timeSinceStartup - t0 > 180) { Debug.LogError("[McpBootstrap] server never became reachable"); return; }
                await Task.Delay(500);
            }

            bool ok = await MCPServiceLocator.Bridge.StartAsync();
            Debug.Log(ok ? "[McpBootstrap] BRIDGE CONNECTED on " + Url : "[McpBootstrap] bridge StartAsync returned false");
        }
        catch (Exception e) { Debug.LogError("[McpBootstrap] " + e); }
    }
}
