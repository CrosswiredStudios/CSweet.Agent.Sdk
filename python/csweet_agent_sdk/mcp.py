"""MCP connection helpers that do not place credentials in prompts or tool arguments."""

from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Any


@dataclass(frozen=True)
class McpConnectionInfo:
    endpoint: str
    access_token: str
    expires_at: datetime
    grant_revision: int
    granted_requested_capabilities: tuple[str, ...]

    @classmethod
    def from_registration(cls, registration: Any) -> "McpConnectionInfo":
        timestamp = registration.mcp_token_expires_at
        expires_at = datetime.fromtimestamp(
            timestamp.seconds + timestamp.nanos / 1_000_000_000, tz=timezone.utc
        )
        if not registration.accepted or not registration.mcp_endpoint or not registration.mcp_access_token:
            raise ValueError("Broker registration did not include a valid MCP session")
        return cls(
            registration.mcp_endpoint,
            registration.mcp_access_token,
            expires_at,
            registration.grant_revision,
            tuple(registration.granted_requested_capabilities),
        )

    def header_provider(self) -> dict[str, str]:
        if self.expires_at <= datetime.now(timezone.utc):
            raise RuntimeError("The MCP access token has expired; reconnect to the broker")
        return {"Authorization": f"Bearer {self.access_token}"}

    def create_maf_tool(self, *, name: str = "csweet-platform") -> Any:
        """Create Python MAF's Streamable HTTP MCP tool without exposing the token to the model."""
        from agent_framework import MCPStreamableHTTPTool

        return MCPStreamableHTTPTool(
            name=name,
            url=self.endpoint,
            header_provider=self.header_provider,
        )
