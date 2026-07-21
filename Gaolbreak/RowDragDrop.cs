using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace Gaolbreak;

internal sealed unsafe class RowDragDrop<T> where T : unmanaged, IEquatable<T>
{
    private readonly string payloadType;
    private readonly Func<T, T, bool> landsBelow;
    private readonly Action<T, T> onDrop;
    private T? source;
    private Vector4 tooltipTextColor;

    public RowDragDrop(string payloadType, Func<T, T, bool> landsBelow, Action<T, T> onDrop)
    {
        this.payloadType = payloadType;
        this.landsBelow = landsBelow;
        this.onDrop = onDrop;
    }

    public void Begin()
    {
        if (ImGui.GetDragDropPayload().IsNull)
            source = null;
        tooltipTextColor = *ImGui.GetStyleColorVec4(ImGuiCol.Text);
    }

    public bool IsSource(T id) => source.HasValue && source.Value.Equals(id);

    // Must be inside a unique PushId scope.
    public RowDisposable Row(T id, ImU8String label, bool draggable)
    {
        return new(this, id, label, draggable);
    }

    internal readonly ref struct RowDisposable : IDisposable
    {
        private readonly RowDragDrop<T> self;
        private readonly T id;
        private readonly ImU8String label;
        private readonly bool draggable;
        private readonly Vector2 pos;
        private readonly ImRaii.DisabledDisposable disabled;

        public RowDisposable(RowDragDrop<T> self, T id, ImU8String label, bool draggable)
        {
            this.self = self;
            this.id = id;
            this.label = label;
            this.draggable = draggable;
            pos = ImGui.GetCursorScreenPos();
            disabled = ImRaii.Disabled(self.IsSource(id));
        }

        public void Dispose()
        {
            disabled.Dispose();
            ImGui.SetCursorScreenPos(pos);
            self.DrawRow(id, label, draggable);
        }
    }

    private void DrawRow(T id, ImU8String label, bool draggable)
    {
        ImGui.Selectable("##drag", false, ImGuiSelectableFlags.SpanAllColumns, new Vector2(0, ImGui.GetFrameHeight()));

        if (draggable)
        {
            using var dragSource = ImRaii.DragDropSource();
            if (dragSource.Success)
            {
                T payloadValue = id;
                ImGui.SetDragDropPayload(payloadType, new ReadOnlySpan<byte>(&payloadValue, sizeof(T)), ImGuiCond.None);
                source = id;

                using (ImRaii.PushColor(ImGuiCol.Text, tooltipTextColor))
                {
                    using (ImRaii.PushFont(UiBuilder.IconFont))
                        ImGui.TextUnformatted(FontAwesomeIcon.GripLines.ToIconString());
                    ImGui.SameLine();
                    ImGui.TextUnformatted(label.Span);
                }
            }
        }
        label.Recycle();

        using var dragTarget = ImRaii.DragDropTarget();
        if (!dragTarget.Success) return;

        var payload = ImGui.AcceptDragDropPayload(payloadType, ImGuiDragDropFlags.AcceptBeforeDelivery | ImGuiDragDropFlags.AcceptNoDrawDefaultRect);
        if (payload.IsNull || payload.DataSize != sizeof(T)) return;
        T sourceId = *(T*)payload.Data;

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        float y = landsBelow(sourceId, id) ? max.Y : min.Y;
        uint color = ImGui.GetColorU32(ImGuiCol.DragDropTarget);
        var drawList = ImGui.GetForegroundDrawList();
        var scale = ImGuiHelpers.GlobalScale;
        var radius = 4f * scale;
        var thickness = 2 * scale;
        drawList.AddCircle(new Vector2(min.X, y), radius, color, 0, thickness);
        drawList.AddLine(new Vector2(min.X + radius, y), new Vector2(max.X, y), color, thickness);

        if (payload.Delivery)
            onDrop(sourceId, id);
    }
}
