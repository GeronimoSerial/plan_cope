import { forwardRef, type ButtonHTMLAttributes } from "react";

type Variant = "primary" | "secondary" | "ghost" | "danger";

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant;
  size?: "md" | "sm";
  block?: boolean;
}

const variantClass: Record<Variant, string> = {
  primary: "",
  secondary: "button--secondary",
  ghost: "button--ghost",
  danger: "button--danger"
};

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(function Button(
  { variant = "primary", size = "md", block = false, className = "", type = "button", children, ...rest },
  ref
) {
  const classes = [
    "button",
    variantClass[variant],
    size === "sm" ? "button--sm" : "",
    block ? "button--block" : "",
    className
  ]
    .filter(Boolean)
    .join(" ");

  return (
    <button ref={ref} type={type} className={classes} {...rest}>
      {children}
    </button>
  );
});
