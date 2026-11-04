import { clsx, type ClassValue } from "clsx";
import { twMerge } from "tailwind-merge";

// Magic UI 官方组件共用的类名合并入口。
export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}
