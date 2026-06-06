(() => {
	const config = window.mapHubConfig;
	if (!config || !config.mapElementId) {
		return;
	}

	const mapElement = document.getElementById(config.mapElementId);
	if (!mapElement) {
		return;
	}

	const searchInput = document.getElementById("points-search");
	const pointItems = Array.from(document.querySelectorAll(".point-item"));
	const categoryButtons = Array.from(document.querySelectorAll(".cat-pill"));
	const sortChips = Array.from(document.querySelectorAll(".chip"));
	const floatButtons = Array.from(document.querySelectorAll(".float-btn"));
	const mapActionButtons = Array.from(document.querySelectorAll(".map-actions button"));
	const toast = document.getElementById("ui-toast");
	const saveViewButton = document.getElementById("save-map-view");
	const exportButton = document.getElementById("export-points");
	const detailPanel = document.getElementById("point-detail");
	const detailName = document.getElementById("detail-name");
	const detailCategory = document.getElementById("detail-category");
	const detailDescription = document.getElementById("detail-description");
	const detailRating = document.getElementById("detail-rating");
	const detailCount = document.getElementById("detail-count");
	const detailReviews = document.getElementById("detail-reviews");
	const detailMediaList = document.getElementById("detail-media-list");
	const detailMediaUpload = document.getElementById("detail-media-upload");
	const detailMediaFile = document.getElementById("detail-media-file");
	const detailMediaSubmit = document.getElementById("detail-media-submit");
	const detailRate = document.getElementById("detail-rate");
	const detailComment = document.getElementById("detail-comment");
	const detailSubmit = document.getElementById("detail-submit");
	const detailClose = document.getElementById("detail-close");
	const starButtons = Array.from(document.querySelectorAll(".rate-stars .star"));
	const chatLog = document.getElementById("chat-log");
	const chatText = document.getElementById("chat-text");
	const chatSend = document.getElementById("chat-send");
	const nameInput = document.getElementById("new-point-name");
	const categoryInput = document.getElementById("new-point-category");
	const descriptionInput = document.getElementById("new-point-description");
	const latitudeInput = document.getElementById("new-point-latitude");
	const longitudeInput = document.getElementById("new-point-longitude");
	const aiButton = document.getElementById("ai-suggest-btn");
	const aiStatus = document.getElementById("ai-status");

	const apiKey = mapElement.dataset.apiKey;

	// debug info: element size and api key
	console.debug("mapHub:init", { apiKey, mapElementId: config.mapElementId, width: mapElement.offsetWidth, height: mapElement.offsetHeight });

	const points = JSON.parse(mapElement.dataset.points || "[]");
	const center = {
		lat: Number(mapElement.dataset.lat),
		lng: Number(mapElement.dataset.lng)
	};
	const zoom = Number(mapElement.dataset.zoom) || 13;
	const savedView = readSavedView();
	const viewCenter = savedView ? savedView.center : center;
	const viewZoom = savedView ? savedView.zoom : zoom;
	const markerEntries = [];
	let originalOrder = [...pointItems];
	let activeCategory = "";
	let activeSort = "popular";
	let leafletMap = null;
	let leafletBounds = null;
	let googleMap = null;
	let googleBounds = null;
	let selectedStar = 0;
	let currentPointId = null;
	const pointsById = new Map(points.filter((p) => p.id).map((p) => [p.id, p]));

	function showMapError(message) {
		console.info('mapHub:showMapError', message);
		let card = mapElement.closest('.map-card');
		if (!card) card = mapElement.parentElement;
		if (!card) return;
		let existing = card.querySelector('.map-error');
		if (existing) {
			existing.textContent = message;
			return;
		}
		const el = document.createElement('div');
		el.className = 'map-error';
		el.style.position = 'absolute';
		el.style.right = '1rem';
		el.style.top = '1rem';
		el.style.zIndex = '30';
		el.style.padding = '0.5rem 0.8rem';
		el.style.background = 'rgba(255,255,255,0.95)';
		el.style.border = '1px solid #e4d6c9';
		el.style.borderRadius = '8px';
		el.style.boxShadow = '0 6px 18px rgba(17,33,35,0.08)';
		el.textContent = message;
		card.style.position = 'relative';
		card.appendChild(el);
	}

	function flashButton(button) {
		if (!button) return;
		button.classList.add("action-active");
		setTimeout(() => button.classList.remove("action-active"), 260);
	}

	function showToast(message) {
		if (!toast) return;
		toast.textContent = message;
		toast.classList.add("show");
		setTimeout(() => toast.classList.remove("show"), 1400);
	}

	function requireLogin(message) {
		if (config.isAuthenticated) return true;
		showToast(message || "Tinh nang Pro. Vui long dang nhap hoac nang cap.");
		if (config.loginUrl) {
			setTimeout(() => {
				window.location.href = config.loginUrl;
			}, 650);
		}
		return false;
	}

	function requireDatabase(message) {
		if (!config.isDemoData) return true;
		showToast(message || "Chua co database nen tinh nang nay chua hoat dong.");
		return false;
	}

	function getPointItems() {
		return Array.from(document.querySelectorAll(".point-item"));
	}

	async function loadWeather(lat, lng) {
		try {
			const response = await fetch(`/Weather/At?lat=${lat}&lng=${lng}`);
			if (!response.ok) return;
			const data = await response.json();
			return data;
		} catch {
			return null;
		}
	}

	function appendChat(author, message) {
		if (!chatLog) return;
		const container = document.createElement("div");
		container.className = "chat-message";
		container.innerHTML = `<strong>${escapeHtml(author)}</strong><span>${escapeHtml(message)}</span>`;
		chatLog.appendChild(container);
		chatLog.scrollTop = chatLog.scrollHeight;
	}

	function setStars(value) {
		selectedStar = value;
		starButtons.forEach((button) => {
			const starValue = Number(button.dataset.star || 0);
			button.classList.toggle("active", starValue <= selectedStar);
		});
	}

	function openDetailPanel() {
		if (!detailPanel) return;
		detailPanel.classList.remove("hidden");
	}

	function closeDetailPanel() {
		if (!detailPanel) return;
		detailPanel.classList.add("hidden");
	}

	async function loadPointDetail(point) {
		if (!detailPanel || !detailName || !detailCategory || !detailDescription || !detailRating || !detailCount || !detailReviews) {
			return;
		}

		const id = Number(point?.id || 0);
		currentPointId = id || null;
		openDetailPanel();

		if (!id) {
			detailName.textContent = point?.name || "Điểm minh họa";
			detailCategory.textContent = point?.category || "";
			detailDescription.textContent = point?.description || "Chưa có mô tả chi tiết.";
			detailRating.textContent = "0.0★";
			detailCount.textContent = "Chưa có đánh giá";
			detailReviews.innerHTML = "<div class=\"detail-review\">Điểm minh họa, chưa có đánh giá.</div>";
			if (detailMediaList) detailMediaList.innerHTML = "";
			if (detailMediaUpload) detailMediaUpload.style.display = config.isAuthenticated ? "grid" : "none";
			if (detailRate) detailRate.style.display = (config.isAuthenticated && !config.isDemoData) ? "grid" : "none";
			return;
		}

		try {
			const response = await fetch(`/MapPoints/Details/${id}`);
			if (!response.ok) {
				throw new Error("Không tải được chi tiết");
			}
			const data = await response.json();
			detailName.textContent = data.name;
			detailCategory.textContent = data.category;
			detailDescription.textContent = data.description || "Chưa có mô tả chi tiết.";
			detailRating.textContent = `${Number(data.averageRating).toFixed(1)}★`;
			detailCount.textContent = `${data.reviewCount} đánh giá`;
			if (detailRate) detailRate.style.display = (config.isAuthenticated && !config.isDemoData) ? "grid" : "none";
			if (detailMediaUpload) detailMediaUpload.style.display = (config.isAuthenticated && !config.isDemoData) ? "grid" : "none";

			if (!data.reviews || data.reviews.length === 0) {
				detailReviews.innerHTML = "<div class=\"detail-review\">Chưa có đánh giá. Hãy là người đầu tiên!</div>";
				return;
			}

			detailReviews.innerHTML = data.reviews.map((review) => {
				const safeComment = escapeHtml(review.comment || "");
				return `<div class=\"detail-review\"><strong>${escapeHtml(review.userName)}</strong><span>${review.rating}★</span><p>${safeComment}</p></div>`;
			}).join("");

			const weather = await loadWeather(data.latitude, data.longitude);
			if (weather) {
				const weatherBlock = `<div class=\"detail-review\"><strong>Thoi tiet hien tai</strong><p>${weather.summary} - ${weather.temperatureC}°C, gio ${weather.windKph} km/h</p></div>`;
				detailReviews.insertAdjacentHTML("afterbegin", weatherBlock);
			}

			await loadMedia(id);
		} catch {
			showToast("Không lấy được chi tiết điểm.");
		}
	}

	async function loadMedia(pointId) {
		if (!detailMediaList) return;
		detailMediaList.innerHTML = "<span class=\"helper-text\">Dang tai anh...</span>";
		try {
			const response = await fetch(`/Media/ByPoint/${pointId}`);
			if (!response.ok) throw new Error("Media load fail");
			const items = await response.json();
			if (!Array.isArray(items) || items.length === 0) {
				detailMediaList.innerHTML = "<span class=\"helper-text\">Chua co anh.</span>";
				return;
			}
			detailMediaList.innerHTML = items.map((item) => {
				const safeUrl = escapeHtml(item.url || "");
				return `<a class=\"media-item\" href=\"${safeUrl}\" target=\"_blank\" rel=\"noopener\"><img src=\"${safeUrl}\" alt=\"Anh dia diem\" /></a>`;
			}).join("");
		} catch {
			detailMediaList.innerHTML = "<span class=\"helper-text\">Khong tai duoc anh.</span>";
		}
	}

	function readSavedView() {
		try {
			const raw = window.localStorage?.getItem("mapHubView");
			if (!raw) return null;
			const parsed = JSON.parse(raw);
			if (!parsed?.center || typeof parsed.zoom !== "number") return null;
			return parsed;
		} catch {
			return null;
		}
	}

	function saveCurrentView() {
		const view = getCurrentView();
		if (!view) return;
		window.localStorage?.setItem("mapHubView", JSON.stringify(view));
		showToast("Đã lưu khung nhìn bản đồ.");
	}

	function getCurrentView() {
		if (googleMap) {
			const center = googleMap.getCenter();
			if (!center) return null;
			return { center: { lat: center.lat(), lng: center.lng() }, zoom: googleMap.getZoom() ?? viewZoom };
		}
		if (leafletMap) {
			const center = leafletMap.getCenter();
			return { center: { lat: center.lat, lng: center.lng }, zoom: leafletMap.getZoom() };
		}
		return null;
	}

	if (searchInput) {
		searchInput.addEventListener("input", () => {
			const query = searchInput.value.trim().toLowerCase();
			applyFilters(query, activeCategory);
		});
	}

	categoryButtons.forEach((button) => {
		button.addEventListener("click", () => {
			const nextCategory = (button.dataset.category || "").trim();
			activeCategory = activeCategory === nextCategory ? "" : nextCategory;
			categoryButtons.forEach((btn) => btn.classList.toggle("active", btn === button && activeCategory));
			const query = (searchInput?.value || "").trim().toLowerCase();
			applyFilters(query, activeCategory);
		});
	});

	pointItems.forEach((item) => {
		item.addEventListener("click", () => {
			const id = Number(item.dataset.id || 0);
			const point = pointsById.get(id) || {
				id,
				name: item.dataset.name || "",
				category: item.dataset.category || "",
				description: item.querySelector("p:last-of-type")?.textContent || ""
			};
			loadPointDetail(point);
		});
	});

	sortChips.forEach((chip) => {
		chip.addEventListener("click", () => {
			const nextSort = (chip.dataset.sort || "popular").trim();
			activeSort = nextSort || "popular";
			sortChips.forEach((btn) => btn.classList.toggle("active", btn === chip));
			applySort(activeSort);
		});
	});

	floatButtons.forEach((button) => {
		button.addEventListener("click", () => {
			flashButton(button);
			switch ((button.dataset.action || "").trim()) {
				case "show-all":
					resetFilters();
					fitToBounds();
					showToast("Đã hiển thị tất cả điểm.");
					break;
				case "filters":
					mapElement.scrollIntoView({ behavior: "smooth", block: "start" });
					showToast("Đã mở bộ lọc.");
					break;
				case "save":
					if (!requireLogin("Tinh nang Pro. Vui long dang nhap de su dung.")) return;
					setAiStatus("Đã bật chế độ tiết kiệm (demo).", false);
					showToast("Đã bật chế độ tiết kiệm.");
					break;
				case "ai":
					if (!requireLogin("Tinh nang AI yeu cau dang nhap.")) return;
					nameInput?.focus();
					aiButton?.click();
					showToast("Đang gọi AI gợi ý...");
					break;
				default:
					break;
			}
		});
	});

	mapActionButtons.forEach((button) => {
		button.addEventListener("click", () => {
			flashButton(button);
			switch ((button.dataset.action || "").trim()) {
				case "map":
					resetFilters();
					fitToBounds();
					showToast("Chế độ bản đồ đang bật.");
					break;
				case "featured":
					applySort("top");
					showToast("Đang hiển thị nổi bật.");
					break;
				case "checkin":
					if (!requireLogin("Can dang nhap de check-in va dung tinh nang Pro.")) return;
					if (!requireDatabase("Chua co database nen khong the check-in.")) return;
					if (!currentPointId) {
						showToast("Chọn một điểm trước khi check-in.");
						break;
					}
					fetch(`/Checkins/At/${currentPointId}`, { method: "POST" })
						.then((response) => {
							if (!response.ok) throw new Error();
							showToast("Đã check-in.");
						})
						.catch(() => showToast("Check-in thất bại."));
					break;
				case "add":
					if (!requireLogin("Can dang nhap de them dia diem.")) return;
					nameInput?.focus();
					showToast("Điền tên để thêm địa điểm.");
					break;
				default:
					break;
			}
		});
	});

	if (saveViewButton) {
		saveViewButton.addEventListener("click", () => {
			if (!requireLogin("Tinh nang Pro. Vui long dang nhap de su dung.")) return;
			flashButton(saveViewButton);
			saveCurrentView();
		});
	}

	if (detailClose) {
		detailClose.addEventListener("click", closeDetailPanel);
	}

	starButtons.forEach((button) => {
		button.addEventListener("click", () => {
			setStars(Number(button.dataset.star || 0));
		});
	});

	if (detailSubmit) {
		detailSubmit.addEventListener("click", async () => {
			if (!config.isAuthenticated) {
				showToast("Vui lòng đăng nhập để đánh giá.");
				return;
			}
			if (!requireDatabase("Chua co database nen khong the danh gia.")) {
				return;
			}
			if (!currentPointId) {
				showToast("Chưa chọn điểm để đánh giá.");
				return;
			}
			if (selectedStar < 1) {
				showToast("Chọn số sao trước khi gửi.");
				return;
			}
			try {
				const response = await fetch("/MapPoints/Rate", {
					method: "POST",
					headers: {
						"Content-Type": "application/json"
					},
					body: JSON.stringify({
						pointId: currentPointId,
						rating: selectedStar,
						comment: detailComment?.value || ""
					})
				});
				if (!response.ok) {
					throw new Error("Không gửi được đánh giá");
				}
				showToast("Đã gửi đánh giá.");
				const point = pointsById.get(currentPointId);
				if (point) {
					await loadPointDetail(point);
				}
				setStars(0);
				if (detailComment) detailComment.value = "";
			} catch {
				showToast("Gửi đánh giá thất bại.");
			}
		});
	}

	if (detailMediaSubmit) {
		detailMediaSubmit.addEventListener("click", async () => {
			if (!config.isAuthenticated) {
				showToast("Vui long dang nhap de tai anh.");
				return;
			}
			if (!requireDatabase("Chua co database nen khong the tai anh.")) {
				return;
			}
			if (!currentPointId) {
				showToast("Chua chon diem de tai anh.");
				return;
			}
			const file = detailMediaFile?.files?.[0];
			if (!file) {
				showToast("Chon anh truoc khi tai len.");
				return;
			}

			const formData = new FormData();
			formData.append("file", file);
			try {
				const response = await fetch(`/Media/Upload/${currentPointId}`, {
					method: "POST",
					body: formData
				});
				if (!response.ok) {
					throw new Error("Upload failed");
				}
				showToast("Tai anh thanh cong.");
				if (detailMediaFile) detailMediaFile.value = "";
				await loadMedia(currentPointId);
			} catch {
				showToast("Tai anh that bai.");
			}
		});
	}

	if (chatSend) {
		chatSend.addEventListener("click", async () => {
			if (!requireLogin("AI Chat la tinh nang Pro. Vui long dang nhap.")) return;
			const prompt = (chatText?.value || "").trim();
			if (!prompt) {
				return;
			}
			appendChat("Ban", prompt);
			chatText.value = "";
			try {
				const response = await fetch("/Ai/Chat", {
					method: "POST",
					headers: { "Content-Type": "application/json" },
					body: JSON.stringify({ prompt })
				});
				if (!response.ok) {
					throw new Error("AI error");
				}
				const data = await response.json();
				appendChat("AI", data.answer || "AI khong tra loi.");
			} catch {
				appendChat("AI", "Khong the goi AI luc nay.");
			}
		});
	}

	if (exportButton) {
		exportButton.addEventListener("click", () => {
			if (!requireLogin("Tinh nang Pro. Vui long dang nhap de su dung.")) return;
			flashButton(exportButton);
			const rows = ["name,category,description,latitude,longitude"];
			points.forEach((point) => {
				const row = [
					escapeCsv(point.name),
					escapeCsv(point.category),
					escapeCsv(point.description || ""),
					point.latitude,
					point.longitude
				].join(",");
				rows.push(row);
			});
			const blob = new Blob([rows.join("\n")], { type: "text/csv;charset=utf-8;" });
			const url = URL.createObjectURL(blob);
			const link = document.createElement("a");
			link.href = url;
			link.download = "cityscout-points.csv";
			document.body.appendChild(link);
			link.click();
			link.remove();
			URL.revokeObjectURL(url);
			showToast("Đã xuất CSV.");
		});
	}

	if (aiButton) {
		aiButton.addEventListener("click", async () => {
			const name = (nameInput?.value || "").trim();
			if (!name) {
				setAiStatus("Vui lòng nhập tên địa điểm trước khi dùng AI.", true);
				nameInput?.focus();
				return;
			}

			const params = new URLSearchParams({
				name,
				category: (categoryInput?.value || "").trim(),
				description: (descriptionInput?.value || "").trim()
			});

			setAiStatus("Đang lấy gợi ý AI...", false);

			try {
				const response = await fetch(`${config.suggestUrl}?${params.toString()}`, {
					method: "GET",
					headers: {
						Accept: "application/json"
					}
				});

				if (!response.ok) {
					throw new Error("Không lấy được gợi ý AI");
				}

				const data = await response.json();
				if (categoryInput && data.category) {
					categoryInput.value = data.category;
				}
				if (descriptionInput && data.description) {
					descriptionInput.value = data.description;
				}

				const sourceLabel = data.source === "external" ? "AI model" : "AI nội bộ";
				setAiStatus(`Đã gợi ý xong (${sourceLabel}).`, false);
			} catch {
				setAiStatus("Không lấy được gợi ý AI. Bạn có thể nhập thủ công.", true);
			}
		});
	}

	const disableGoogle = window.localStorage?.getItem('mapHubDisableGoogle') === '1';
	if (apiKey && !disableGoogle) {
		loadGoogleMap();
	} else {
		loadLeafletMap();
	}

	function loadGoogleMap() {
		let googleInitTimeout = null;
		window.gm_authFailure = () => {
			console.warn('mapHub:google:auth-failure');
			showMapError('Google Maps bi tu choi key/API. Dang chuyen sang ban do du phong.');
			window.localStorage?.setItem('mapHubDisableGoogle', '1');
			loadLeafletMap();
		};
		window.initMapHubMap = () => {
			if (googleInitTimeout) {
				clearTimeout(googleInitTimeout);
				googleInitTimeout = null;
			}
			const map = new google.maps.Map(mapElement, {
				center: viewCenter,
				zoom: viewZoom,
				zoomControl: true,
				mapTypeControl: false,
				fullscreenControl: true,
				streetViewControl: false,
				gestureHandling: 'greedy'
			});

			googleMap = map;

			try {
				// rest of init code continues below
			} catch (err) {
				console.error('mapHub:google:init:error', err);
				showMapError('Lỗi khi khởi tạo Google Maps. Chuyển sang bản đồ thay thế.');
				loadLeafletMap();
				return;
			}

			const bounds = points.length ? new google.maps.LatLngBounds() : null;
			points.forEach((point) => {
				const marker = new google.maps.Marker({
					map,
					position: {
						lat: point.latitude,
						lng: point.longitude
					},
					title: point.name,
					draggable: !!(nameInput && latitudeInput && longitudeInput)
				});

				if (bounds) {
					bounds.extend({ lat: point.latitude, lng: point.longitude });
				}

				const infoWindow = new google.maps.InfoWindow({
					content: `<div class="gm-info"><h3>${escapeHtml(point.name)}</h3><p>${escapeHtml(point.category)}</p><p>${escapeHtml(point.description || "")}</p></div>`
				});

				marker.addListener("click", () => {
					infoWindow.open({
						map,
						anchor: marker
					});
					applyPointToForm(point);
					loadPointDetail(point);
				});

				marker.addListener("dragend", (event) => {
					if (!event.latLng) return;
					if (latitudeInput) latitudeInput.value = event.latLng.lat().toFixed(6);
					if (longitudeInput) longitudeInput.value = event.latLng.lng().toFixed(6);
					showToast("Đã cập nhật tọa độ từ marker.");
				});

				markerEntries.push({
					searchText: `${(point.name || "").toLowerCase()} ${(point.category || "").toLowerCase()}`,
					category: (point.category || "").toLowerCase(),
					setVisible: (isVisible) => marker.setVisible(isVisible)
				});
			});

			googleBounds = bounds;

			if (bounds) {
				map.fitBounds(bounds, 40);
			} else {
				map.setCenter(center);
			}

			setTimeout(() => {
				try {
					if (bounds) {
						map.fitBounds(bounds, 40);
					}
				} catch (e) {
					// ignore
				}
			}, 800);

			if (latitudeInput && longitudeInput) {
				map.addListener("click", (event) => {
					if (!event.latLng) {
						return;
					}

					latitudeInput.value = event.latLng.lat().toFixed(6);
					longitudeInput.value = event.latLng.lng().toFixed(6);
				});
			}

			// Ensure the map tiles render correctly after initialization
			google.maps.event.addListenerOnce(map, 'idle', () => {
				try {
					google.maps.event.trigger(map, 'resize');
					if (bounds && map.getZoom() > 15) {
						map.setZoom(15);
					}
				} catch (e) {
					// ignore
				}
			});

			google.maps.event.addListener(map, "idle", () => {
				const view = getCurrentView();
				if (!view) return;
				window.localStorage?.setItem("mapHubLastView", JSON.stringify(view));
			});

			// If Google tiles never render, fallback to Leaflet
			setTimeout(() => {
				const hasGoogleTiles = !!mapElement.querySelector('.gm-style');
				if (!hasGoogleTiles) {
					console.warn('mapHub:google:no-tiles -> falling back to leaflet');
					showMapError('Google Maps khong render duoc. Dang chuyen sang ban do du phong.');
					window.localStorage?.setItem('mapHubDisableGoogle', '1');
					loadLeafletMap();
				}
			}, 2500);
		};

		const script = document.createElement("script");
		script.src = `https://maps.googleapis.com/maps/api/js?key=${encodeURIComponent(apiKey)}&callback=initMapHubMap`;
		script.async = true;
		script.defer = true;
		script.onerror = () => {
			console.warn('mapHub:google:script:error');
			showMapError('Không thể tải Google Maps script. Đang dùng bản đồ dự phòng.');
			window.localStorage?.setItem('mapHubDisableGoogle', '1');
			loadLeafletMap();
		};
		document.head.appendChild(script);

		// If Google API doesn't initialize within X ms, fallback to Leaflet
		googleInitTimeout = setTimeout(() => {
			if (!window.google || !window.google.maps) {
				console.warn('mapHub:google:timeout -> falling back to leaflet');
				showMapError('Google Maps không trả lời. Đang chuyển sang bản đồ dự phòng.');
				window.localStorage?.setItem('mapHubDisableGoogle', '1');
				loadLeafletMap();
			}
		}, 6000);
	}

	async function loadLeafletMap() {
		mapElement.innerHTML = '';
		await ensureLeaflet();

		const map = window.L.map(mapElement).setView([viewCenter.lat, viewCenter.lng], viewZoom);
		leafletMap = map;
		window.L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
			maxZoom: 19,
			attribution: "&copy; OpenStreetMap contributors"
		}).addTo(map);

		// add compact controls similar to Google Maps
		if (map.zoomControl) {
			map.zoomControl.remove();
		}
		window.L.control.zoom({ position: 'topright' }).addTo(map);
		window.L.control.scale({ position: 'bottomright' }).addTo(map);

		const bounds = points.length ? window.L.latLngBounds([]) : null;
		points.forEach((point) => {
			const marker = window.L.marker([point.latitude, point.longitude], {
				draggable: !!(nameInput && latitudeInput && longitudeInput)
			}).addTo(map);
			marker.bindPopup(`<div class="gm-info"><h3>${escapeHtml(point.name)}</h3><p>${escapeHtml(point.category)}</p><p>${escapeHtml(point.description || "")}</p></div>`);
			marker.on("click", () => {
				applyPointToForm(point);
				loadPointDetail(point);
			});
			marker.on("dragend", () => {
				const pos = marker.getLatLng();
				if (latitudeInput) latitudeInput.value = pos.lat.toFixed(6);
				if (longitudeInput) longitudeInput.value = pos.lng.toFixed(6);
				showToast("Đã cập nhật tọa độ từ marker.");
			});

			if (bounds) {
				bounds.extend([point.latitude, point.longitude]);
			}

			markerEntries.push({
				searchText: `${(point.name || "").toLowerCase()} ${(point.category || "").toLowerCase()}`,
				category: (point.category || "").toLowerCase(),
				setVisible: (isVisible) => {
					const hasLayer = map.hasLayer(marker);
					if (isVisible && !hasLayer) {
						marker.addTo(map);
					}
					if (!isVisible && hasLayer) {
						map.removeLayer(marker);
					}
				}
			});
		});

		leafletBounds = bounds;

		if (bounds) {
			map.fitBounds(bounds, { padding: [40, 40] });
		}

		if (latitudeInput && longitudeInput) {
			map.on("click", (event) => {
				latitudeInput.value = event.latlng.lat.toFixed(6);
				longitudeInput.value = event.latlng.lng.toFixed(6);
			});
		}

		// ensure leaflet recalculates size after being added to DOM
		setTimeout(() => {
			try {
				map.invalidateSize(true);
				if (bounds) {
					map.fitBounds(bounds, { padding: [40, 40] });
				} else {
					map.setView([center.lat, center.lng], zoom);
				}
			} catch (e) {
				// ignore
			}
		}, 200);

		map.on("moveend", () => {
			const view = getCurrentView();
			if (!view) return;
			window.localStorage?.setItem("mapHubLastView", JSON.stringify(view));
		});

		setTimeout(() => {
			try {
				if (bounds) {
					map.fitBounds(bounds, { padding: [40, 40] });
				}
			} catch (e) {
				// ignore
			}
		}, 800);
	}

	function ensureLeaflet() {
		if (window.L) {
			return Promise.resolve();
		}

		return new Promise((resolve, reject) => {
			const cssId = "leaflet-css";
			if (!document.getElementById(cssId)) {
				const css = document.createElement("link");
				css.id = cssId;
				css.rel = "stylesheet";
				css.href = "https://unpkg.com/leaflet@1.9.4/dist/leaflet.css";
				document.head.appendChild(css);
			}

			const script = document.createElement("script");
			script.src = "https://unpkg.com/leaflet@1.9.4/dist/leaflet.js";
			script.async = true;
			script.onload = resolve;
			script.onerror = reject;
			document.head.appendChild(script);
		});
	}

	function escapeHtml(value) {
		return String(value)
			.replace(/&/g, "&amp;")
			.replace(/</g, "&lt;")
			.replace(/>/g, "&gt;")
			.replace(/\"/g, "&quot;")
			.replace(/'/g, "&#39;");
	}

	function filterPointList(query, categoryFilter) {
		getPointItems().forEach((item) => {
			const name = (item.dataset.name || "").toLowerCase();
			const categoryValue = (item.dataset.category || "").toLowerCase();
			const matchesQuery = !query || name.includes(query) || categoryValue.includes(query);
			const matchesCategory = !categoryFilter || categoryValue.includes(categoryFilter.toLowerCase());
			const isVisible = matchesQuery && matchesCategory;
			item.style.display = isVisible ? "block" : "none";
		});
	}

	function filterMapMarkers(query, category) {
		markerEntries.forEach((entry) => {
			const matchesQuery = !query || entry.searchText.includes(query);
			const matchesCategory = !category || entry.category.includes(category.toLowerCase());
			const isVisible = matchesQuery && matchesCategory;
			entry.setVisible(isVisible);
		});
	}

	function applyFilters(query, category) {
		filterPointList(query, category);
		filterMapMarkers(query, category);
	}

	function applyPointToForm(point) {
		if (!nameInput) return;
		nameInput.value = point.name || "";
		if (categoryInput) categoryInput.value = point.category || "";
		if (descriptionInput) descriptionInput.value = point.description || "";
		if (latitudeInput) latitudeInput.value = Number(point.latitude).toFixed(6);
		if (longitudeInput) longitudeInput.value = Number(point.longitude).toFixed(6);
		showToast("Đã nạp thông tin điểm vào form.");
	}

	function escapeCsv(value) {
		const text = String(value ?? "");
		if (/[",\n]/.test(text)) {
			return `"${text.replace(/"/g, '""')}"`;
		}
		return text;
	}

	function resetFilters() {
		activeCategory = "";
		categoryButtons.forEach((btn) => btn.classList.remove("active"));
		if (searchInput) {
			searchInput.value = "";
		}
		applyFilters("", "");
		applySort("popular");
		sortChips.forEach((btn) => btn.classList.toggle("active", btn.dataset.sort === "popular"));
	}

	function fitToBounds() {
		if (googleMap && googleBounds) {
			googleMap.fitBounds(googleBounds, 40);
			return;
		}
		if (leafletMap && leafletBounds) {
			leafletMap.fitBounds(leafletBounds, { padding: [40, 40] });
		}
	}

	if (window.ResizeObserver) {
		const resizeObserver = new ResizeObserver(() => {
			if (leafletMap) {
				leafletMap.invalidateSize();
			}
			if (googleMap) {
				google.maps.event.trigger(googleMap, 'resize');
			}
		});
		resizeObserver.observe(mapElement);
	}

	function applySort(mode) {
		const items = getPointItems();
		if (!items.length) {
			return;
		}

		if (items.length !== originalOrder.length) {
			originalOrder = [...items];
		}

		const list = items[0].parentElement;
		if (!list) {
			return;
		}

		let sortedItems = [...items];
		if (mode === "top") {
			sortedItems.sort((a, b) => {
				const aName = (a.dataset.name || "").toLowerCase();
				const bName = (b.dataset.name || "").toLowerCase();
				return aName.localeCompare(bName);
			});
		} else if (mode === "new") {
			sortedItems = [...originalOrder].reverse();
		} else {
			sortedItems = [...originalOrder];
		}

		sortedItems.forEach((item) => list.appendChild(item));
	}

	function setAiStatus(message, isError) {
		if (!aiStatus) {
			return;
		}

		aiStatus.textContent = message;
		aiStatus.classList.toggle("error", !!isError);
	}
})();

// ── Tùy biến mọi ô chọn file → nút "📷 Chọn ảnh" + tên file (đồng bộ toàn site) ──
(function () {
	function enhanceFileInputs(root) {
		(root || document).querySelectorAll('input[type="file"]:not([data-enhanced])').forEach(function (input) {
			input.setAttribute("data-enhanced", "1");
			input.style.display = "none";

			var wrap = document.createElement("div");
			wrap.style.cssText = "display:flex;align-items:center;gap:.6rem;flex-wrap:wrap";

			var btn = document.createElement("button");
			btn.type = "button";
			btn.textContent = "📷 Chọn ảnh";
			btn.style.cssText = "padding:.45rem .9rem;border:1px solid #e2e8f0;border-radius:8px;background:#fff;cursor:pointer;font-weight:600;font-size:.9rem;color:#475569";

			var name = document.createElement("span");
			name.textContent = "Chưa chọn ảnh";
			name.style.cssText = "font-size:.85rem;color:#94a3b8";

			input.parentNode.insertBefore(wrap, input);
			wrap.appendChild(btn);
			wrap.appendChild(name);
			wrap.appendChild(input);

			btn.addEventListener("click", function () { input.click(); });
			input.addEventListener("change", function () {
				var has = input.files && input.files.length;
				name.textContent = has ? input.files[0].name : "Chưa chọn ảnh";
				name.style.color = has ? "#16a34a" : "#94a3b8";
			});
		});
	}
	document.addEventListener("DOMContentLoaded", function () { enhanceFileInputs(document); });
	document.addEventListener("shown.bs.modal", function (e) { enhanceFileInputs(e.target); });
})();

/* ── Premium motion: scroll-reveal + subtle pointer tilt (CSS-driven, nhẹ) ── */
(() => {
	const reduce = window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;

	function initReveal() {
		const els = document.querySelectorAll(".reveal");
		if (!els.length) return;
		if (reduce || !("IntersectionObserver" in window)) {
			els.forEach(el => el.classList.add("in"));
			return;
		}
		const io = new IntersectionObserver((entries) => {
			entries.forEach(e => {
				if (e.isIntersecting) { e.target.classList.add("in"); io.unobserve(e.target); }
			});
		}, { threshold: 0.12, rootMargin: "0px 0px -8% 0px" });
		els.forEach(el => io.observe(el));
	}

	// Tilt 3D rất nhẹ (≤3°), chỉ thiết bị có chuột; bỏ qua nếu giảm chuyển động
	function initTilt() {
		if (reduce || window.matchMedia("(hover: none)").matches) return;
		document.querySelectorAll("[data-tilt]").forEach(card => {
			let raf = null;
			card.style.transformStyle = "preserve-3d";
			card.style.transition = "transform .2s cubic-bezier(.22,1,.36,1)";
			card.addEventListener("pointermove", (ev) => {
				const r = card.getBoundingClientRect();
				const px = (ev.clientX - r.left) / r.width - 0.5;
				const py = (ev.clientY - r.top) / r.height - 0.5;
				if (raf) cancelAnimationFrame(raf);
				raf = requestAnimationFrame(() => {
					card.style.transform = `perspective(900px) rotateY(${px * 4}deg) rotateX(${-py * 4}deg) translateY(-4px)`;
				});
			});
			card.addEventListener("pointerleave", () => {
				if (raf) cancelAnimationFrame(raf);
				card.style.transform = "";
			});
		});
	}

	// Animated counters (.kpi-num[data-count], data-money="1" để format tiền)
	function initCounters() {
		document.querySelectorAll(".kpi-num[data-count]").forEach((el) => {
			if (el._counted) return; el._counted = true;
			const target = parseFloat(el.dataset.count) || 0;
			const money = el.dataset.money === "1";
			const fmt = (v) => money ? Math.round(v).toLocaleString("vi-VN") + "đ" : Math.round(v).toLocaleString("vi-VN");
			if (reduce) { el.textContent = fmt(target); return; }
			const dur = 900, t0 = performance.now();
			function tick(now) { const p = Math.min((now - t0) / dur, 1); el.textContent = fmt(target * (1 - Math.pow(1 - p, 3))); if (p < 1) requestAnimationFrame(tick); }
			requestAnimationFrame(tick);
		});
	}

	function init() { initReveal(); initTilt(); initCounters(); }
	if (document.readyState !== "loading") init();
	else document.addEventListener("DOMContentLoaded", init);
})();

/* ── Validate SĐT (9–11 chữ số) cho mọi form có input .phone-in ──────────── */
(() => {
	document.addEventListener("submit", (e) => {
		const form = e.target;
		if (!form.querySelectorAll) return;
		const bad = Array.from(form.querySelectorAll(".phone-in")).find((inp) => {
			const d = (inp.value || "").replace(/\D/g, "");
			return inp.value.trim() && (d.length < 9 || d.length > 11);
		});
		if (bad) { e.preventDefault(); alert("Số điện thoại phải có 9–11 chữ số."); bad.focus(); }
	}, true);
})();
