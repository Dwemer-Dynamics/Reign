using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Reign.Mcp.Server;

[McpServerToolType]
public static class DeploymentSourceTools
{
    [McpServerTool(Name = "reign_check_deployment_source", ReadOnly = true, Destructive = false,
        Idempotent = true, UseStructuredContent = true)]
    [Description("Checks original canonical Release reports, current source fingerprints and report-bound artifacts without building, deploying, starting the game or changing the deployment authority.")]
    public static DeploymentSourceCheck CheckDeploymentSource(ReignValidationService validation,
        string validationReport = "", string linuxBuildReport = "") =>
        validation.CheckDeploymentSource(validationReport, linuxBuildReport);
}
