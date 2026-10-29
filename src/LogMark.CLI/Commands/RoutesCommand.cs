namespace LogMark.CLI.Commands;

public class RoutesCommand : ICommand
{
    public string Name => "routes";
    public string Description => "List configured routes";
    public string Usage => "routes";
    public bool RequiresVault => true;

    public string[]? Examples => new[]
    {
        "lm routes",
        "lm --json routes"
    };

    public int Execute(CliContext context, string[] args)
    {
        var routes = context.RouteService.GetRoutes();

        if (context.JsonOutput)
        {
            var result = new
            {
                routes = routes.Select(r => new
                {
                    name = r.Name,
                    path = r.Path,
                    section = r.Section
                })
            };
            var json = System.Text.Json.JsonSerializer.Serialize(result,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            context.WriteResult(json);
        }
        else
        {
            if (routes.Count == 0)
            {
                context.WriteResult("No routes configured.");
                context.WriteInfo("Add routes to logmark/_system/routes.md");
                return 0;
            }

            context.WriteResult("Routes:");
            foreach (var route in routes)
            {
                context.WriteResult($"  +{route.Name,-20} {route.Path}");
            }
        }

        return 0;
    }
}
