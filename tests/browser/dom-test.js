await (async () => {
  const check = (condition, message) => {
    if (!condition) throw new Error(message);
  };
  const marker = () =>
    document.querySelector("#lab05-marker-target").getAttribute("data-lab05-marker");
  const get = async (id) => {
    const response = await fetch(`/api/incidents/${encodeURIComponent(id)}`);
    check(response.status === 200, `Unexpected HTTP ${response.status}`);
    return response.json();
  };
  const read = async (id) => {
    document.querySelector("#incident-id").value = id;
    await load();
    await new Promise((resolve) => setTimeout(resolve, 500));
  };

  check(marker() === null, "Reload before test");

  // 1) Description
  const descriptionId = document.querySelector("#incident-id").value.trim();
  check(descriptionId.length > 0, "Enter the description fixture Guid");
  const stored = await get(descriptionId);
  check(stored.description.includes("data-lab05-marker"), "Wrong description fixture");
  await read(descriptionId);
  const box = document.querySelector("#description");
  check(box.textContent === stored.description, "Description text changed");
  check(box.childElementCount === 0, "Description became HTML elements");
  check(marker() === null, "Marker executed via description");

  // 2) Comments і повторний render
  const seedId = "20000000-0000-0000-0000-000000000001";
  const seeded = await get(seedId);
  const texts = seeded.comments.map((comment) => comment.text);
  check(texts.some((text) => text.includes("data-lab05-marker")), "Wrong comment fixture");
  await read(seedId);
  await read(seedId);
  const items = [...document.querySelectorAll("#comments p")];
  check(items.length === texts.length, "Comment nodes duplicated or missing");
  items.forEach((paragraph, index) => {
    check(paragraph.textContent === texts[index], "Comment text changed");
    check(paragraph.childElementCount === 0, "Comment became HTML elements");
  });
  check(marker() === null, "Marker executed via comment");

  console.log("PASS: description and comments rendered as plain text; re-render without duplicates");
})();