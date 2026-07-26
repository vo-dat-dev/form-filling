using Microsoft.Agents.AI;

public interface IAgentFactory
{
    string Route { get; }
    AIAgent CreateAgent();
}
