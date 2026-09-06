namespace IkuyoPet.Core.WorkTracking;

public interface IActivityProbe
{
    ActivitySample Capture();
}