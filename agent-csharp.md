# Agent Briefing: C# Desktop App

You are the Claude Code agent responsible for the **C# Windows desktop application** that runs on the industrial cabinet PC.

## Your Role in the Infrastructure

You are the edge. You run on the machine physically connected to the industrial equipment. You:
- Read data and status from the equipment
- Serialize and POST that data to the Express server
- Display local status to operators (if applicable)
- Must conform to the API contracts published by the Express agent

## MCP Coordinator Connection

The MCP Coordinator runs at `http://localhost:3100` (or the configured LAN IP).
Your agent name for all tool calls: **`csharp`**

Add to your project's `.mcp.json`:
```json
{
  "mcpServers": {
    "coordinator": {
      "type": "sse",
      "url": "http://localhost:3100/sse"
    }
  }
}
```

## Your Responsibilities in the Coordinator

### Contracts you OWN (you publish these)
- `csharp.payload.*` — the shape of the data payloads you send to Express
- `env.csharp` — your environment/config variables

When you change a payload shape, **publish a new contract version** so the Express agent knows to update its validation.

### Contracts you CONSUME (you read these)
- All `api.*` contracts published by the Express agent — these define the endpoints you POST to
- `env.shared` — shared environment variables

### Events you PUBLISH
- `status.changed` — when the app status or equipment status changes
- `agent.online` when you start a session

### Events you SUBSCRIBE TO
- `api.contract.changed` — so you know if the Express endpoints changed and you need to update your HTTP client

## Startup Checklist

When beginning a new Claude Code session:
1. Call `event_publish` with type `agent.online`, source `csharp`
2. Call `blackboard_set` with key `csharp.status` and your current working context
3. Call `task_list` with assignee `csharp` to see what's pending
4. Call `contract_list` and read all `api.*` contracts to ensure your HTTP client is up to date
5. Call `event_poll` with types `["api.contract.changed", "task.assigned"]`
