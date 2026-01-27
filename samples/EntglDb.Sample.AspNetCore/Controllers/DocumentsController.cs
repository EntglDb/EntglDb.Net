using EntglDb.Core;
using EntglDb.Core.Storage;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Linq.Expressions;

namespace EntglDb.Sample.AspNetCore.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase
{
    private readonly IPeerDatabase _db;

    public DocumentsController(IPeerDatabase db)
    {
        _db = db;
    }

    [HttpGet("{collection}/{id}")]
    public async Task<IActionResult> GetDocument(string collection, string id)
    {
        var col = _db.Collection(collection);
        // Using object or JsonElement depending on serializer behavior. 
        // EntglDb uses System.Text.Json usually. 
        var doc = await col.Get<JsonElement>(id);
        
        // Check if doc is ValueKind.Undefined (default struct) to determine "not found"? 
        // Or generic Get returns default(T)? JsonElement default is Undefined.
        if (doc.ValueKind == JsonValueKind.Undefined)
            return NotFound();

        return Ok(doc);
    }

    [HttpPost("{collection}")]
    public async Task<IActionResult> SaveDocument(string collection, [FromBody] JsonElement content)
    {
        string id = content.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString();
        
        var col = _db.Collection(collection);
        await col.Put(id, content);
        
        return Ok(new { Message = "Saved", Id = id });
    }

    [HttpGet("{collection}")]
    public async Task<IActionResult> ListDocuments(string collection)
    {
        var col = _db.Collection(collection);
        // Find all. Using a dummy expression that is true.
        // Identify if expression translator supports it.
        // Assuming simple scan is supported.
        Expression<Func<JsonElement, bool>> predicate = x => true;
        var docs = await col.Find(predicate);
        return Ok(docs);
    }
}
