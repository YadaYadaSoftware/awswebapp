using Amazon.Lambda.Annotations;
using Amazon.Lambda.Annotations.APIGateway;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TaskManager.Data;
using TaskManager.Shared.Models;

namespace TaskManager.Api.Controllers;

public class ProjectsController
{
    private readonly TaskManagerDbContext _context;

    public ProjectsController(TaskManagerDbContext context)
    {
        _context = context;
    }

    [LambdaFunction]
    [HttpApi(LambdaHttpMethod.Get, "/api/projects")]
    public async Task<APIGatewayProxyResponse> GetProjects(ILambdaContext context)
    {
        try
        {
            var projects = await _context.Projects
                .Where(p => p.IsActive)
                .Include(p => p.Owner)
                .Include(p => p.Tasks)
                .Include(p => p.Members)
                    .ThenInclude(m => m.User)
                .Select(p => new ProjectDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Description = p.Description,
                    OwnerId = p.OwnerId,
                    OwnerName = $"{p.Owner.FirstName} {p.Owner.LastName}",
                    CreatedAt = p.CreatedAt,
                    UpdatedAt = p.UpdatedAt,
                    IsActive = p.IsActive,
                    Members = p.Members.Select(m => new ProjectMemberDto
                    {
                        ProjectId = m.ProjectId,
                        UserId = m.UserId,
                        UserName = $"{m.User.FirstName} {m.User.LastName}",
                        UserEmail = m.User.Email,
                        Role = m.Role,
                        JoinedAt = m.JoinedAt
                    }).ToList(),
                    Tasks = p.Tasks.Select(t => new TaskDto
                    {
                        Id = t.Id,
                        Title = t.Title,
                        Description = t.Description,
                        ProjectId = t.ProjectId,
                        ProjectName = p.Name,
                        AssignedToId = t.AssignedToId,
                        AssignedToName = t.AssignedTo != null ? $"{t.AssignedTo.FirstName} {t.AssignedTo.LastName}" : null,
                        Status = t.Status,
                        Priority = t.Priority,
                        DueDate = t.DueDate,
                        CreatedAt = t.CreatedAt,
                        UpdatedAt = t.UpdatedAt
                    }).ToList()
                })
                .ToListAsync();

            return new APIGatewayProxyResponse
            {
                StatusCode = 200,
                Body = JsonSerializer.Serialize(projects),
                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
            };
        }
        catch (Exception ex)
        {
            context.Logger.LogError($"Error getting projects: {ex.Message}");
            return new APIGatewayProxyResponse
            {
                StatusCode = 500,
                Body = JsonSerializer.Serialize(new { error = "An error occurred while retrieving projects" }),
                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
            };
        }
    }

    [LambdaFunction]
    [HttpApi(LambdaHttpMethod.Get, "/api/projects/{id}")]
    public async Task<APIGatewayProxyResponse> GetProject(string id, ILambdaContext context)
    {
        try
        {
            if (!Guid.TryParse(id, out var projectId))
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = 400,
                    Body = JsonSerializer.Serialize(new { error = "Invalid project ID format" }),
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                };
            }

            var project = await _context.Projects
                .Where(p => p.Id == projectId && p.IsActive)
                .Include(p => p.Owner)
                .Include(p => p.Tasks)
                    .ThenInclude(t => t.AssignedTo)
                .Include(p => p.Members)
                    .ThenInclude(m => m.User)
                .Select(p => new ProjectDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Description = p.Description,
                    OwnerId = p.OwnerId,
                    OwnerName = $"{p.Owner.FirstName} {p.Owner.LastName}",
                    CreatedAt = p.CreatedAt,
                    UpdatedAt = p.UpdatedAt,
                    IsActive = p.IsActive,
                    Members = p.Members.Select(m => new ProjectMemberDto
                    {
                        ProjectId = m.ProjectId,
                        UserId = m.UserId,
                        UserName = $"{m.User.FirstName} {m.User.LastName}",
                        UserEmail = m.User.Email,
                        Role = m.Role,
                        JoinedAt = m.JoinedAt
                    }).ToList(),
                    Tasks = p.Tasks.Select(t => new TaskDto
                    {
                        Id = t.Id,
                        Title = t.Title,
                        Description = t.Description,
                        ProjectId = t.ProjectId,
                        ProjectName = p.Name,
                        AssignedToId = t.AssignedToId,
                        AssignedToName = t.AssignedTo != null ? $"{t.AssignedTo.FirstName} {t.AssignedTo.LastName}" : null,
                        Status = t.Status,
                        Priority = t.Priority,
                        DueDate = t.DueDate,
                        CreatedAt = t.CreatedAt,
                        UpdatedAt = t.UpdatedAt
                    }).ToList()
                })
                .FirstOrDefaultAsync();

            if (project == null)
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = 404,
                    Body = JsonSerializer.Serialize(new { error = $"Project with ID {id} not found" }),
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                };
            }

            return new APIGatewayProxyResponse
            {
                StatusCode = 200,
                Body = JsonSerializer.Serialize(project),
                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
            };
        }
        catch (Exception ex)
        {
            context.Logger.LogError($"Error getting project {id}: {ex.Message}");
            return new APIGatewayProxyResponse
            {
                StatusCode = 500,
                Body = JsonSerializer.Serialize(new { error = "An error occurred while retrieving the project" }),
                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
            };
        }
    }

    [LambdaFunction]
    [HttpApi(LambdaHttpMethod.Post, "/api/projects")]
    public async Task<APIGatewayProxyResponse> CreateProject([FromBody] CreateProjectRequest request, ILambdaContext context)
    {
        try
        {
            // TODO: Get current user from authentication context
            // For now, we'll use a placeholder user ID
            var currentUserId = Guid.NewGuid(); // This should come from authentication

            var project = new Data.Entities.Project
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                Description = request.Description,
                OwnerId = currentUserId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsActive = true
            };

            _context.Projects.Add(project);
            await _context.SaveChangesAsync();

            // Return the created project
            var createdProject = await _context.Projects
                .Where(p => p.Id == project.Id)
                .Include(p => p.Owner)
                .Select(p => new ProjectDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Description = p.Description,
                    OwnerId = p.OwnerId,
                    OwnerName = $"{p.Owner.FirstName} {p.Owner.LastName}",
                    CreatedAt = p.CreatedAt,
                    UpdatedAt = p.UpdatedAt,
                    IsActive = p.IsActive
                })
                .FirstOrDefaultAsync();

            return new APIGatewayProxyResponse
            {
                StatusCode = 201,
                Body = JsonSerializer.Serialize(createdProject),
                Headers = new Dictionary<string, string>
                {
                    { "Content-Type", "application/json" },
                    { "Location", $"/api/projects/{project.Id}" }
                }
            };
        }
        catch (Exception ex)
        {
            context.Logger.LogError($"Error creating project: {ex.Message}");
            return new APIGatewayProxyResponse
            {
                StatusCode = 500,
                Body = JsonSerializer.Serialize(new { error = "An error occurred while creating the project" }),
                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
            };
        }
    }
}