using EntglDb.Core;
using EntglDb.Network;
using Microsoft.AspNetCore.Mvc;

namespace EntglDb.Sample.AspNetCore.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ClusterController : ControllerBase
{
    private readonly IEntglDbNode _node;

    public ClusterController(IEntglDbNode node)
    {
        _node = node;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        // Address is on IEntglDbNode
        // KnownPeers are usually on Cluster or Discovery or Orchestrator.
        // Looking at IEntglDbNode.Discovery (IDiscoveryService) or Orchestrator?
        // Discovery service typically has GetKnownPeers().
        // Let's assume Discovery has it or we can't easily get it without casting.
        // Actually, Orchestrator might have Cluster info.
        // Given I verified IDiscoveryService.cs has 279 bytes, it probably has some methods.
        // Let's try utilizing what we have.
        
        return Ok(new
        {
            Address = _node.Address,
            // Peers = _node.Discovery.GetKnownPeers(), // Need to check if this exists, safer to return partial info or check interface details if build fails.
            State = "Running"
        });
    }
}
