"""Plain JSON-RPC client for the Unity MCP HTTP bridge (127.0.0.1:8090).

One tool call per process. Initialises a session, pins the real project's editor instance
(selection is session-scoped; with a stray second instance connected the server refuses every
call until one is chosen), then runs the requested tool or resource read.

  python mcp_call.py --list
  python mcp_call.py --resource mcpforunity://editor/state
  python mcp_call.py read_console '{"action":"get","types":["error"],"count":20,"format":"plain"}'
  python mcp_call.py execute_menu_item '{"menu_path":"VibeGame1/Health Check"}'
  python mcp_call.py execute_code '{"action":"execute","code":"return 1+1;"}'
"""
import sys, json, re, urllib.request

BASE = "http://127.0.0.1:8090/mcp"
PROJECT = "vibegame1"


def post(body, sid=None):
    h = {"Content-Type": "application/json", "Accept": "application/json, text/event-stream"}
    if sid:
        h["Mcp-Session-Id"] = sid
    req = urllib.request.Request(BASE, data=json.dumps(body).encode(), headers=h, method="POST")
    r = urllib.request.urlopen(req, timeout=300)
    sid = r.headers.get("Mcp-Session-Id", sid)
    raw = r.read().decode("utf-8", "replace")
    data = None
    if raw.lstrip().startswith("{"):
        data = json.loads(raw)
    else:
        for line in raw.splitlines():
            if line.startswith("data:"):
                try:
                    data = json.loads(line[5:].strip())
                except Exception:
                    pass
    return sid, data


def pin_instance(sid):
    try:
        _, inst = post({"jsonrpc": "2.0", "id": 98, "method": "resources/read",
                        "params": {"uri": "mcpforunity://instances"}}, sid)
        names = re.findall(PROJECT + r"@[0-9a-f]+", json.dumps(inst))
        if names:
            post({"jsonrpc": "2.0", "id": 99, "method": "tools/call",
                  "params": {"name": "set_active_instance", "arguments": {"instance": names[0]}}}, sid)
    except Exception:
        pass


def main():
    sid, _ = post({"jsonrpc": "2.0", "id": 1, "method": "initialize",
                   "params": {"protocolVersion": "2025-03-26", "capabilities": {},
                              "clientInfo": {"name": "cc", "version": "1"}}})
    post({"jsonrpc": "2.0", "method": "notifications/initialized"}, sid)
    pin_instance(sid)
    a = sys.argv[1:]
    if not a:
        print(__doc__)
        return
    if a[0] == "--list":
        _, d = post({"jsonrpc": "2.0", "id": 2, "method": "tools/list"}, sid)
        for t in d["result"]["tools"]:
            print(t["name"])
        return
    if a[0] == "--resource":
        _, d = post({"jsonrpc": "2.0", "id": 2, "method": "resources/read", "params": {"uri": a[1]}}, sid)
    else:
        args = json.loads(a[1]) if len(a) > 1 else {}
        _, d = post({"jsonrpc": "2.0", "id": 2, "method": "tools/call",
                     "params": {"name": a[0], "arguments": args}}, sid)
    res = d.get("result", d)
    if isinstance(res, dict) and "content" in res:
        for c in res["content"]:
            print(c.get("text", json.dumps(c)))
    else:
        print(json.dumps(res, indent=1))


main()
