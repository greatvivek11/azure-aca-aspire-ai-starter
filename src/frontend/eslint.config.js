import js from "@eslint/js";
import react from "eslint-plugin-react";
import reactHooks from "eslint-plugin-react-hooks";
import reactRefresh from "eslint-plugin-react-refresh";
import globals from "globals";

export default [
	{
		ignores: ["dist/**", "node_modules/**"],
	},
	{
		files: ["**/*.{js,jsx,mjs,cjs}"],
		languageOptions: {
			ecmaVersion: "latest",
			sourceType: "module",
			parserOptions: {
				ecmaFeatures: {
					jsx: true,
				},
			},
			globals: {
				...globals.browser,
				...globals.node,
			},
		},
		plugins: {
			react,
			"react-hooks": reactHooks,
			"react-refresh": reactRefresh,
		},
		rules: {
			...js.configs.recommended.rules,
			...react.configs.recommended.rules,
			...reactHooks.configs.recommended.rules,
			"react/jsx-uses-react": "off",
			"react/react-in-jsx-scope": "off",
			"react-refresh/only-export-components": [
				"warn",
				{ allowConstantExport: true },
			],
			"no-console": "off",
		},
		settings: {
			react: {
				version: "detect",
			},
		},
	},
];
