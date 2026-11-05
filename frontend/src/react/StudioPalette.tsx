import { useId } from "react";
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
  ["homework", BookOpen, "同步已发布的作业，可按学科筛选。"],
  ["todo", Bell, "显示已发布的班级提醒和待办。"],
  ["timetable", CalendarDays, "显示当天课表和当前课程。"],
  ["datetime", Clock3, "显示班级名称、日期和当前时间。"],
  ["countdown", Timer, "按指定日期显示距离活动还有几天。"],
  ["message", MessageSquare, "展示班级寄语或每日一句。"],
  ["text", Type, "输入任意文字，用作标题或说明。"],
  ["image", Image, "上传图片，可用作班徽或插图。"],
  ["shape", Square, "添加形状，用于装饰或划分区域。"],
] as const;

/** 直接组合 Magic UI 官方 BentoGrid / MagicCard；业务事件交回 Vue 画布。 */
export function StudioPalette({
  disabled,
  onAdd,
}: {
  disabled: boolean;
  onAdd: (kind: WidgetKind) => void;
}) {
  const descriptionId = useId();
  return (
    <BentoGrid className="studio-palette grid-cols-2 auto-rows-auto gap-2">
      {entries.map(([kind, Icon, description]) => (
        <MagicCard
          key={kind}
          className="studio-magic-card rounded-2xl"
          gradientColor="#d9e8fc"
          gradientOpacity={0.6}
          gradientFrom="#86b7f5"
          gradientTo="#c2b5eb"
          gradientSize={150}
        >
          <button
            type="button"
            disabled={disabled}
            aria-describedby={`${descriptionId}-${kind}`}
            onClick={() => onAdd(kind)}
          >
            <span className="studio-palette-icon" aria-hidden="true">
              <Icon size={19} />
            </span>
            <span>{widgetLabels[kind]}</span>
            <span
              id={`${descriptionId}-${kind}`}
              className="studio-palette-description"
              aria-hidden="true"
            >
              {description}
            </span>
            <Plus size={12} />
          </button>
        </MagicCard>
      ))}
    </BentoGrid>
  );
}
