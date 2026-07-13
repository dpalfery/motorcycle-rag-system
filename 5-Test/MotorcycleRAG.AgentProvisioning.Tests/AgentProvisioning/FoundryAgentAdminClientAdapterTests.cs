using System;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.AI.Projects.Agents;
using Moq;
using MotorcycleRAG.AgentProvisioning.Azure;
using Xunit;
using OpenAI.Responses;

namespace MotorcycleRAG.UnitTests.AgentProvisioning;

public class FoundryAgentAdminClientAdapterTests
{
    [Fact]
    public void Constructor_NullClient_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new FoundryAgentAdminClientAdapter(null!));
    }
}
