using TuckPane.Models;

namespace TuckPane.Core;

internal readonly record struct StationTransitionFrame(
    double ClipLeft,
    double ClipTop,
    double ClipRight,
    double ClipBottom,
    double TranslationX,
    double TranslationY,
    double Opacity);

internal static class StationTransitionMath
{
    internal const double ContentTravelDip = 20;

    internal static StationTransitionFrame GetFrame(
        OrganizerDockEdge edge,
        double widthDip,
        double heightDip,
        double progress,
        double minimumRevealDip,
        bool reducedMotion)
    {
        double width = Math.Max(1, widthDip);
        double height = Math.Max(1, heightDip);
        double normalized = Math.Clamp(progress, 0, 1);
        if (reducedMotion)
        {
            return new(0, 0, width, height, 0, 0, normalized);
        }

        double minimumReveal = Math.Clamp(minimumRevealDip, 0, edge is OrganizerDockEdge.Left or OrganizerDockEdge.Right ? width : height);
        double reveal = edge is OrganizerDockEdge.Left or OrganizerDockEdge.Right
            ? minimumReveal + (width - minimumReveal) * normalized
            : minimumReveal + (height - minimumReveal) * normalized;
        double remainingTravel = ContentTravelDip * (1 - normalized);

        return edge switch
        {
            OrganizerDockEdge.Left => new(0, 0, reveal, height, -remainingTravel, 0, normalized),
            OrganizerDockEdge.Top => new(0, 0, width, reveal, 0, -remainingTravel, normalized),
            OrganizerDockEdge.Right => new(width - reveal, 0, width, height, remainingTravel, 0, normalized),
            _ => new(0, height - reveal, width, height, 0, remainingTravel, normalized)
        };
    }
}
