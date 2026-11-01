namespace CasCap.Attributes;

/// <summary>
/// Marks an MCP tool method whose call must be approved by a person in the chat group before it runs.
/// </summary>
/// <remarks>
/// Enforced by the call-tool filter registered with
/// <see cref="Microsoft.Extensions.DependencyInjection.ToolApprovalMcpServerBuilderExtensions.WithToolApproval"/>.
/// <see cref="CasCap.Models.ToolApprovalConfig.WaivedTools"/> and
/// <see cref="CasCap.Models.ToolApprovalConfig.RequiredTools"/> override the attribute per tool.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class RequiresApprovalAttribute : Attribute;
