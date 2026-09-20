const status = document.querySelector('#component-status');
document.querySelectorAll('button').forEach(button => {
  button.addEventListener('click', () => {
    document.querySelectorAll('button').forEach(item => item.classList.remove('selected'));
    button.classList.add('selected');
    status.textContent = `组件反馈：${button.textContent.trim().replace(/\s+/g, ' ')}（设计演示）`;
  });
});
