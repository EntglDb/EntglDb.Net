using EntglDb.Sample.Shared;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EntglDb.Sample.AspNetCore.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase
{
    private readonly SampleDbContext _db;

    public DocumentsController(SampleDbContext db)
    {
        _db = db;
    }

    [HttpGet("{collection}/{id}")]
    public IActionResult GetDocument(string collection, string id)
    {
        return collection.ToLower() switch
        {
            "users" => _db.Users.FindById(id) is { } u ? Ok(u) : NotFound(),
            "todolists" => _db.TodoLists.FindById(id) is { } t ? Ok(t) : NotFound(),
            _ => NotFound()
        };
    }

    [HttpPost("{collection}")]
    public async Task<IActionResult> SaveDocument(string collection, [FromBody] JsonElement content)
    {
        string id = content.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString();

        switch (collection.ToLower())
        {
            case "users":
                var user = JsonSerializer.Deserialize<User>(content.GetRawText());
                if (user == null) return BadRequest();
                user.Id = id;
                await _db.Users.InsertAsync(user);
                break;
            case "todolists":
                var list = JsonSerializer.Deserialize<TodoList>(content.GetRawText());
                if (list == null) return BadRequest();
                list.Id = id;
                await _db.TodoLists.InsertAsync(list);
                break;
            default:
                return NotFound();
        }
        await _db.SaveChangesAsync();
        return Ok(new { Message = "Saved", Id = id });
    }

    [HttpGet("{collection}")]
    public IActionResult ListDocuments(string collection)
    {
        return collection.ToLower() switch
        {
            "users" => Ok(_db.Users.FindAll().ToList()),
            "todolists" => Ok(_db.TodoLists.FindAll().ToList()),
            _ => NotFound()
        };
    }
}
