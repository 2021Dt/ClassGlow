export {};
declare module "vue" {
  export interface GlobalComponents {
    AppSelect: typeof import("./components/AppSelect")["default"];
  }
}
