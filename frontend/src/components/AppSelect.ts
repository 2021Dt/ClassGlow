import {
  defineComponent,
  h,
  isVNode,
  onMounted,
  ref,
  useId,
  type ComponentPublicInstance,
  type VNode,
} from "vue";
import { Check, ChevronDown } from "@lucide/vue";
import {
  SelectRoot,
  SelectTrigger,
  SelectValue,
  SelectIcon,
  SelectPortal,
  SelectContent,
  SelectViewport,
  SelectItem,
  SelectItemText,
} from "reka-ui";

type Option = { value: string | number; label: string; disabled: boolean };
function textOf(value: unknown): string {
  if (Array.isArray(value)) return value.map(textOf).join("");
  if (isVNode(value)) return textOf(value.children);
  return typeof value === "string" || typeof value === "number"
    ? String(value)
    : "";
}
function optionsOf(nodes: VNode[]): Option[] {
  return nodes.flatMap((node) => {
    if (node.type === "option") {
      const label = textOf(node.children).trim();
      return [
        {
          value: node.props?.value ?? label,
          label,
          disabled: node.props?.disabled === "" || !!node.props?.disabled,
        },
      ];
    }
    return Array.isArray(node.children)
      ? optionsOf(node.children.filter(isVNode))
      : [];
  });
}

/** 保留原来的 option、表单校验和 change 接口，由 Reka 负责键盘、焦点与弹层定位。 */
export default defineComponent({
  name: "AppSelect",
  inheritAttrs: false,
  props: {
    modelValue: [String, Number],
    value: [String, Number],
    disabled: Boolean,
    required: Boolean,
    name: String,
  },
  emits: ["update:modelValue", "change"],
  setup(props, { slots, attrs, emit }) {
    const native = ref<HTMLSelectElement>();
    const trigger = ref<ComponentPublicInstance>();
    const host = ref<HTMLElement>();
    const label = ref("");
    const portal = ref<HTMLElement>();
    const open = ref(false);
    const id = useId();
    function button() {
      return trigger.value?.$el as HTMLButtonElement | undefined;
    }
    function setOpen(value: boolean) {
      // 原生 dialog 和日期 popover 属于顶层，菜单必须留在同一层，避免被遮住或误关日历。
      portal.value =
        host.value?.closest<HTMLElement>(
          "[popover]:popover-open, dialog[open]",
        ) ?? document.body;
      open.value = value;
    }
    onMounted(() => {
      const parent = host.value?.closest("label");
      if (parent)
        label.value = Array.from(parent.childNodes)
          .filter((node) => !node.contains(host.value!))
          .map((node) => node.textContent ?? "")
          .join("")
          .replace(/\s+/g, " ")
          .trim();
    });
    return () => {
      const nodes = slots.default?.() ?? [];
      const options = optionsOf(nodes);
      const value = props.modelValue ?? props.value ?? "";
      const selected = options.findIndex(
        (option) => String(option.value) === String(value),
      );
      const { class: outerClass, style, ...controlAttrs } = attrs;
      function changed(event: Event) {
        const index = native.value!.selectedIndex;
        if (index >= 0) emit("update:modelValue", options[index]!.value);
        emit("change", event);
      }
      return h(
        "span",
        { class: ["app-select-field", outerClass], style, ref: host },
        [
          // 不弹出原生菜单；仅用于 required、表单提交及兼容已有事件处理。
          h(
            "select",
            {
              ref: native,
              class: "app-select-native",
              tabindex: -1,
              "aria-hidden": "true",
              value,
              name: props.name,
              required: props.required,
              disabled: props.disabled,
              onChange: changed,
              onFocus: () => button()?.focus(),
              onInvalid: (event: Event) => {
                event.preventDefault();
                button()?.focus();
                setOpen(true);
              },
            },
            nodes,
          ),
          h(
            SelectRoot,
            {
              modelValue: selected >= 0 ? String(selected) : undefined,
              disabled: props.disabled,
              open: open.value,
              "onUpdate:open": setOpen,
              "onUpdate:modelValue": (key: unknown) => {
                const option = options[Number(key)];
                if (!option || !native.value) return;
                native.value.value = String(option.value);
                native.value.dispatchEvent(
                  new Event("change", { bubbles: true }),
                );
                setOpen(false);
              },
            },
            {
              default: () => [
                h(
                  SelectTrigger,
                  {
                    ...controlAttrs,
                    ref: trigger,
                    id: attrs.id ?? id,
                    class: "app-select",
                    type: "button",
                    "aria-label":
                      attrs["aria-label"] ?? (label.value || undefined),
                    "aria-required": props.required || undefined,
                  },
                  {
                    default: () => [
                      h(
                        SelectValue,
                        {},
                        { default: () => options[selected]?.label ?? "请选择" },
                      ),
                      h(
                        SelectIcon,
                        { asChild: true },
                        {
                          default: () =>
                            h(ChevronDown, {
                              size: 15,
                              "aria-hidden": true,
                              class: "app-select-chevron",
                            }),
                        },
                      ),
                    ],
                  },
                ),
                h(
                  SelectPortal,
                  { to: portal.value },
                  {
                    default: () =>
                      h(
                        SelectContent,
                        {
                          class: "app-select-menu",
                          position: "popper",
                          sideOffset: 7,
                          collisionPadding: 12,
                          positionStrategy: "fixed",
                          onKeydown: (event: KeyboardEvent) => {
                            event.stopPropagation();
                            if (event.key === "Escape") {
                              event.preventDefault();
                              setOpen(false);
                            }
                          },
                          onEscapeKeyDown: (event: KeyboardEvent) => {
                            // 阻止浏览器同时关闭外层日期 popover；一次 Esc 只退出当前菜单。
                            event.preventDefault();
                            event.stopPropagation();
                            setOpen(false);
                          },
                        },
                        {
                          default: () =>
                            h(
                              SelectViewport,
                              { class: "app-select-viewport" },
                              {
                                default: () =>
                                  options.map((option, index) =>
                                    h(
                                      SelectItem,
                                      {
                                        key: String(option.value),
                                        value: String(index),
                                        disabled: option.disabled,
                                        textValue: option.label,
                                        class: "app-select-option",
                                      },
                                      {
                                        default: () => [
                                          h(
                                            SelectItemText,
                                            {},
                                            { default: () => option.label },
                                          ),
                                          index === selected
                                            ? h(Check, {
                                                size: 15,
                                                "aria-hidden": true,
                                              })
                                            : null,
                                        ],
                                      },
                                    ),
                                  ),
                              },
                            ),
                        },
                      ),
                  },
                ),
              ],
            },
          ),
        ],
      );
    };
  },
});
