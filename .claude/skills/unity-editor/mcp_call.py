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
import sys, json, re, time, urllib.request

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
    if a[0] == "--schema":
        _, d = post({"jsonrpc": "2.0", "id": 2, "method": "tools/list"}, sid)
        for t in d["result"]["tools"]:
            if len(a) == 1 or t["name"] == a[1]:
                print(json.dumps(t, indent=1))
        return
    if a[0] == "--run-tests":
        mode = a[1] if len(a) > 1 else "EditMode"
        test_args = {"mode": mode, "include_failed_tests": True, "init_timeout": 120000}
        if len(a) > 2:
            test_args["test_names"] = a[2:]
        _, started = post({"jsonrpc": "2.0", "id": 2, "method": "tools/call",
                           "params": {"name": "run_tests", "arguments": test_args}}, sid)
        result = started.get("result", started)
        content = result.get("content", []) if isinstance(result, dict) else []
        payload = json.loads(content[0]["text"]) if content else result
        if isinstance(result, dict) and result.get("structuredContent"):
            payload = result["structuredContent"].get("result", payload)
        if isinstance(payload, dict) and "result" in payload and "data" not in payload:
            payload = payload["result"]
        data = (payload.get("data") or {}) if isinstance(payload, dict) else {}
        job_id = data.get("job_id")
        if not job_id:
            print(json.dumps(payload, indent=1))
            return
        while True:
            _, polled = post({"jsonrpc": "2.0", "id": 3, "method": "tools/call",
                              "params": {"name": "get_test_job", "arguments": {
                                  "job_id": job_id, "include_failed_tests": True,
                                  "wait_timeout": 20}}}, sid)
            result = polled.get("result", polled)
            content = result.get("content", []) if isinstance(result, dict) else []
            payload = json.loads(content[0]["text"]) if content else result
            if isinstance(result, dict) and result.get("structuredContent"):
                payload = result["structuredContent"].get("result", payload)
            if isinstance(payload, dict) and "result" in payload and "data" not in payload:
                payload = payload["result"]
            data = (payload.get("data") or {}) if isinstance(payload, dict) else {}
            if not data.get("status"):
                if isinstance(payload, dict) and payload.get("error") == "TimeoutError":
                    continue
                print(json.dumps(payload, indent=1))
                return
            if data.get("status") not in ("running", "queued"):
                print(json.dumps(payload, indent=1))
                return
            progress = data.get("progress") or {}
            print("tests: {}/{}".format(progress.get("completed", 0),
                                         progress.get("total", "?")), flush=True)
            time.sleep(1)
    if a[0] == "--exec":
        a = ["execute_code", json.dumps({"action": "execute", "code": " ".join(a[1:])})]
    if a[0] == "--menu":
        a = ["execute_menu_item", json.dumps({"menu_path": " ".join(a[1:])})]
    if a[0] == "--clear-tests":
        a = ["run_tests", json.dumps({"clear_stuck": True})]
    if a[0] in ("--play", "--stop"):
        a = ["manage_editor", json.dumps({"action": a[0][2:]})]
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
