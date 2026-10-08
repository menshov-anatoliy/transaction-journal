import * as React from "react";

const MOBILE_QUERY = "(max-width: 1023px)";

export function useIsMobile() {
	const [isMobile, setIsMobile] = React.useState<boolean>(() => {
		if (typeof window === "undefined" || typeof window.matchMedia !== "function") {
			return false;
		}

		return window.matchMedia(MOBILE_QUERY).matches;
	});

	React.useEffect(() => {
		if (typeof window.matchMedia !== "function") {
			return;
		}

		const media = window.matchMedia(MOBILE_QUERY);
		const update = () => setIsMobile(media.matches);
		update();
		media.addEventListener("change", update);
		return () => media.removeEventListener("change", update);
	}, []);

	return isMobile;
}
