using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class ToolMetadataTests
{
    [Fact]
    public void ToolRiskMetadataMatchesTheEnforcedSurface()
    {
        var assembly = typeof(RuntimeTools).Assembly;
        var tools = assembly.GetTypes()
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Select(method => new
            {
                Method = method,
                Tool = method.GetCustomAttribute<McpServerToolAttribute>(),
                Description = method.GetCustomAttribute<DescriptionAttribute>()
            })
            .Where(item => item.Tool is not null)
            .ToArray();
        var writes = new HashSet<string>(StringComparer.Ordinal)
        {
            "reign_build_release_runtime",
            "reign_build_release_package",
            "reign_start_verification",
            "reign_cancel_verification",
            "reign_start_spymaster_test",
            "reign_start_spymaster_organic_test",
            "reign_start_spymaster_organic_destructive_test",
            "reign_set_spymaster_agent_activation_fixture",
            "reign_start_arrest_test",
            "reign_start_arrest_organic_test",
            "reign_start_rebellion_test",
            "reign_start_capital_ambassador_test",
            "reign_start_royal_council_test",
            "reign_start_ruler_docket_test",
            "reign_start_kingdom_event_test",
            "reign_start_government_test",
            "reign_build",
            "reign_validate",
            "reign_run_offline_verification",
            "reign_author_character_narratives"
            ,"reign_cleanup_campaigns_without_saves"
            ,"reign_prepare_social_reputation_test"
            ,"reign_start_social_reputation_test"
            ,"reign_prepare_campaign_command_test"
            ,"reign_verify_campaign_command_disposable_save"
            ,"reign_start_campaign_command_profile"
            ,"reign_verify_campaign_command_save_roundtrip"
            ,"reign_start_campaign_command_certification"
            ,"reign_resume_campaign_command_certification"
            ,"reign_cancel_campaign_command_certification"
            ,"reign_shutdown_server"
            ,"reign_prepare_campaign_test"
            ,"reign_start_campaign_test"
            ,"reign_arm_campaign_test"
            ,"reign_advance_campaign_test"
            ,"reign_stop_campaign_test"
            ,"reign_checkpoint_campaign_test"
            ,"reign_restart_campaign_test"
            ,"reign_restore_campaign_test_checkpoint"
            ,"reign_cleanup_campaign_test"
            ,"reign_prepare_party_agency_test"
            ,"reign_carry_forward_party_agency_passes"
            ,"reign_start_party_agency_test"
            ,"reign_verify_party_agency_save_roundtrip"
            ,"reign_prepare_arrest_certification"
            ,"reign_run_arrest_certification_contract"
            ,"reign_carry_forward_arrest_passes"
            ,"reign_start_arrest_certification_case"
            ,"reign_run_rebellion_certification_contract"
            ,"reign_prepare_rebellion_certification"
            ,"reign_carry_forward_rebellion_passes"
            ,"reign_start_rebellion_certification_case"
            ,"reign_prepare_government_certification"
            ,"reign_carry_forward_government_passes"
            ,"reign_start_government_certification_case"
            ,"reign_record_government_certification_evidence"
        };

        var destructive = new HashSet<string>(StringComparer.Ordinal)
        {
            "reign_start_spymaster_organic_test",
            "reign_start_spymaster_organic_destructive_test",
            "reign_set_spymaster_agent_activation_fixture",
            "reign_start_arrest_organic_test",
            "reign_start_capital_ambassador_test",
            "reign_start_royal_council_test",
            "reign_start_ruler_docket_test",
            "reign_start_kingdom_event_test"
            ,"reign_start_government_test"
            ,"reign_start_social_reputation_test"
            ,"reign_start_campaign_command_profile"
            ,"reign_verify_campaign_command_save_roundtrip"
            ,"reign_start_campaign_command_certification"
            ,"reign_shutdown_server"
            ,"reign_start_campaign_test"
            ,"reign_arm_campaign_test"
            ,"reign_advance_campaign_test"
            ,"reign_checkpoint_campaign_test"
            ,"reign_restart_campaign_test"
            ,"reign_restore_campaign_test_checkpoint"
            ,"reign_cleanup_campaign_test"
            ,"reign_start_party_agency_test"
            ,"reign_verify_party_agency_save_roundtrip"
            ,"reign_start_arrest_certification_case"
            ,"reign_start_rebellion_certification_case"
            ,"reign_start_government_certification_case"
            ,"reign_cleanup_campaigns_without_saves"
        };

        Assert.Equal(114, tools.Length);
        Assert.All(tools, item =>
        {
            Assert.StartsWith("reign_", item.Tool!.Name, StringComparison.Ordinal);
            Assert.NotNull(item.Description);
            Assert.Equal(destructive.Contains(item.Tool.Name!), item.Tool.Destructive);
            Assert.Equal(
                writes.Contains(item.Tool.Name!),
                item.Tool.ReadOnly == false);
        });
    }
}
