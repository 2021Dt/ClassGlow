import {
  BookOpen,
  Bell,
  CalendarDays,
  Clock3,
  Timer,
  MessageSquare,
  Type,
  Image,
  Square,
  Plus,
} from "lucide-react";
import { BentoGrid } from "../components/magicui/bento-grid";
import { MagicCard } from "../components/magicui/magic-card";
import { widgetLabels } from "../canvas";
import type { WidgetKind } from "../domain";

const entries = [
  ["homework", BookOpen],
  ["todo", Bell],
  ["timetable", CalendarDays],
  ["datetime", Clock3],
  ["countdown", Timer],
  ["message", MessageSquare],
  ["text", Type],
  ["image", Image],
  ["shape", Square],
] as const;

/** 直接组合 Magic UI 官方 BentoGrid / MagicCard；业务事件交回 Vue 画布。 */
export function StudioPalette({
  disabled,
  onAdd,
}: {
  disabled: boolean;
  onAdd: (kind: WidgetKind) => void;
}) {
  return (
    <BentoGrid className="studio-palette grid-cols-2 auto-rows-auto gap-2">
      {entries.map(([kind, Icon]) => (
        <MagicCard
          key={kind}
          className="studio-magic-card rounded-2xl"
          gradientColor="#d9e8fc"
          gradientOpacity={0.6}
          gradientFrom="#86b7f5"
          gradientTo="#c2b5eb"
          gradientSize={150}
        >
          <button type="button" disabled={disabled} onClick={() => onAdd(kind)}>
            <span className="studio-palette-icon">
              <Icon size={19} />
            </span>
            <span>{widgetLabels[kind]}</span>
            <Plus size={12} />
          </button>
        </MagicCard>
      ))}
    </BentoGrid>
  );
}
