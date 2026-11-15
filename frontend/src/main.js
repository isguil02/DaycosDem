import { mount } from 'svelte'
import './Global.css'
import App from './App.svelte'

const app = mount(App, {
  target: document.getElementById('app') ?? document.body,
})

export default app
