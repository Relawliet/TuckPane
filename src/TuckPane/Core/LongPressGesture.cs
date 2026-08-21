namespace TuckPane.Core;

public enum LongPressResult
{
    None,
    StartDrag,
    Open,
    FinishDrag,
    Cancel
}

public sealed class LongPressGesture(double movementLimit)
{
    private double _startX;
    private double _startY;
    private bool _active;
    private bool _dragging;
    private bool _moved;

    public void Press(double x, double y)
    {
        _startX = x;
        _startY = y;
        _active = true;
        _dragging = false;
        _moved = false;
    }

    public LongPressResult Move(double x, double y)
    {
        if (!_active || _dragging)
        {
            return LongPressResult.None;
        }
        double distance = Math.Sqrt(Math.Pow(x - _startX, 2) + Math.Pow(y - _startY, 2));
        if (distance <= movementLimit)
        {
            return LongPressResult.None;
        }
        _moved = true;
        return LongPressResult.Cancel;
    }

    public LongPressResult Elapse()
    {
        if (!_active || _dragging)
        {
            return LongPressResult.None;
        }
        _dragging = true;
        return LongPressResult.StartDrag;
    }

    public LongPressResult Release()
    {
        LongPressResult result = !_active
            ? LongPressResult.Cancel
            : _dragging ? LongPressResult.FinishDrag : _moved ? LongPressResult.Cancel : LongPressResult.Open;
        Reset();
        return result;
    }

    public void Reset()
    {
        _active = false;
        _dragging = false;
        _moved = false;
    }
}
